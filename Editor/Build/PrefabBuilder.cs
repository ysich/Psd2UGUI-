using System;
using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Editor.Import;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Psd2Ugui.Editor.Build
{
    /// <summary>一次装配的结果。</summary>
    public sealed class PrefabBuildResult
    {
        /// <summary>场景里临时建出来的根对象，装配完由调用方销毁。</summary>
        public GameObject Root;

        public GameObject Prefab;
        public string PrefabPath = string.Empty;
        public int Nodes;

        public string BuildSummary()
        {
            return "节点 " + Nodes + " 个" + (Prefab == null ? string.Empty : " → " + PrefabPath);
        }
    }

    /// <summary>
    /// 装配计划 → uGUI 对象树 → 预制体资产。
    ///
    /// 这里只做「照着计划建对象、挂组件、连线」这类机械动作，
    /// 控件映射与布局换算都在 Core 的 <see cref="PrefabPlanner"/> 里，可脱离 Unity 测试。
    ///
    /// 对象索引按「层级键」组织（稳定 ID，模板节点用 `#名字`），
    /// 增量更新时用的是同一套键，因此建与改走的是同一段代码。
    /// </summary>
    public static class PrefabBuilder
    {
        /// <summary>在场景里建出对象树（不落盘），用于预览与测试。</summary>
        public static PrefabBuildResult CreateHierarchy(UiDocument document, PlanNode plan,
            PrefabBuildContext context)
        {
            var result = new PrefabBuildResult();
            if (document == null || plan == null)
            {
                return result;
            }

            var index = new Dictionary<string, GameObject>();
            GameObject root = CreateNode(plan, null, context, index, null, true);
            result.Root = root;
            result.Nodes = context.NodesCreated;
            return result;
        }

        /// <summary>建出对象树并保存成预制体资产。</summary>
        public static PrefabBuildResult BuildPrefab(UiDocument document, PlanNode plan,
            PrefabBuildContext context, string prefabPath)
        {
            PrefabBuildResult result = CreateHierarchy(document, plan, context);
            if (result.Root == null)
            {
                return result;
            }

            try
            {
                result.PrefabPath = prefabPath;
                EnsureFolder(prefabPath);
                result.Prefab = PrefabUtility.SaveAsPrefabAsset(result.Root, prefabPath);
                if (result.Prefab == null)
                {
                    context.Report(DiagnosticSeverity.Error, "prefab.save-failed",
                        "预制体保存失败：" + prefabPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(result.Root);
                result.Root = null;
                AssetDatabase.Refresh();
            }

            return result;
        }

        public static void EnsureFolder(string assetFilePath)
        {
            string folder = Path.GetDirectoryName(assetFilePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Psd2UguiPaths.EnsureAssetFolder(folder.Replace('\\', '/'));
            }
        }

        /// <summary>
        /// 建一个节点及其子树，并登记层级键。
        /// 新建与「增量更新里新增的节点」共用这一条路径。
        /// </summary>
        public static GameObject CreateSubtree(PlanNode plan, Transform parent, PrefabBuildContext context,
            Dictionary<string, GameObject> index, string parentKey, bool isRoot = false)
        {
            return CreateNode(plan, parent, context, index, parentKey, isRoot);
        }

        private static GameObject CreateNode(PlanNode plan, Transform parent, PrefabBuildContext context,
            Dictionary<string, GameObject> index, string parentKey, bool isRoot)
        {
            var go = new GameObject(plan.Name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            if (isRoot)
            {
                ApplyRootLayout(rect, plan, context);
            }
            else
            {
                ControlFactory.ApplyLayout(rect, plan);
            }

            ControlFactory.ApplyVisual(plan, go, context);
            context.NodesCreated++;

            string key = PlanKeys.Join(parentKey, PlanKeys.Key(plan));
            index[key] = go;

            if (!string.IsNullOrEmpty(plan.PrefabTarget))
            {
                AttachNestedPrefab(plan, go, context);
                AttachMarker(plan, go, context);
                if (!plan.Active)
                {
                    go.SetActive(false);
                }

                return go;
            }

            for (int i = 0; i < plan.Children.Count; i++)
            {
                CreateNode(plan.Children[i], go.transform, context, index, key, false);
            }

            Wire(plan, go, index, key, context);
            AttachMarker(plan, go, context);

            if (!plan.Active)
            {
                go.SetActive(false);
            }

            return go;
        }

        /// <summary>
        /// 把已经建好的节点按计划刷新一遍（增量更新用）。
        /// 只写工具管的属性：名字、布局、外观、子件连线、标记指纹。
        /// </summary>
        public static void ApplyManaged(PlanNode plan, GameObject go, PrefabBuildContext context,
            Dictionary<string, GameObject> index, string key, bool isRoot = false)
        {
            if (plan == null || go == null)
            {
                return;
            }

            go.name = plan.Name;
            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                if (isRoot)
                {
                    ApplyRootLayout(rect, plan, context);
                }
                else
                {
                    ControlFactory.ApplyLayout(rect, plan);
                }
            }

            RemoveStaleComponents(plan, go, context);
            ControlFactory.ApplyVisual(plan, go, context);

            if (!string.IsNullOrEmpty(plan.PrefabTarget))
            {
                // 子预制体节点：里面是别人的东西，挂着就别动
                AttachNestedPrefab(plan, go, context);
                AttachMarker(plan, go, context);
                go.SetActive(plan.Active);
                context.NodesReused++;
                return;
            }

            Wire(plan, go, index, key, context);
            AttachMarker(plan, go, context);
            go.SetActive(plan.Active);
            context.NodesReused++;
        }

        private static void ApplyRootLayout(RectTransform rect, PlanNode plan, PrefabBuildContext context)
        {
            ControlFactory.ApplyLayout(rect, plan);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            if (!context.Build.RootCanvas)
            {
                return;
            }

            var canvas = rect.gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = rect.gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = rect.gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = rect.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(
                context.Build.ReferenceWidth > 0 ? context.Build.ReferenceWidth : (float)plan.Rect.Width,
                context.Build.ReferenceHeight > 0 ? context.Build.ReferenceHeight : (float)plan.Rect.Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (rect.gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                rect.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>`refp` 节点：挂一个子预制体实例，里面不动。</summary>
        private static void AttachNestedPrefab(PlanNode plan, GameObject go, PrefabBuildContext context)
        {
            string path = Psd2UguiPaths.PrefabPath(context.Export, plan.PrefabTarget);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                context.Report(DiagnosticSeverity.Warning, "prefab.nested-missing",
                    "找不到被引用的子预制体，先留一个空节点占位：" + plan.PrefabTarget + "（" + path + "）");
                return;
            }

            GameObject instance = FindNestedInstance(go, asset, plan.PrefabTarget);
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, go.transform);
                instance.name = plan.PrefabTarget;
            }

            var rect = instance.GetComponent<RectTransform>();
            if (rect != null)
            {
                ControlFactory.ApplyLayout(rect, plan);
            }
        }

        /// <summary>已经挂着同一个子预制体的实例就复用，别每次重新导出都多挂一个。</summary>
        private static GameObject FindNestedInstance(GameObject go, GameObject asset, string name)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject child = go.transform.GetChild(i).gameObject;
                if (PrefabUtility.GetCorrespondingObjectFromSource(child) == asset)
                {
                    return child;
                }

                if (child.name == name && PrefabUtility.IsAnyPrefabInstanceRoot(child))
                {
                    return child;
                }
            }

            return null;
        }

        private static void AttachMarker(PlanNode plan, GameObject go, PrefabBuildContext context)
        {
            if (!context.Build.MarkNodes || string.IsNullOrEmpty(plan.SourceNodeId))
            {
                return;
            }

            var marker = go.GetComponent<Psd2UguiNode>();
            if (marker == null)
            {
                marker = go.AddComponent<Psd2UguiNode>();
            }

            marker.NodeId = plan.SourceNodeId;
            marker.LayerPath = plan.SourceLayerPath;
            marker.LayerId = plan.SourceLayerId;
            marker.Role = plan.Role;
            marker.SourcePsd = context.SourcePsd;
            // 记下这次写了什么，下次导出据此判断「设计稿到底变没变」
            marker.SpecHash = plan.SpecHash;
        }

        /// <summary>
        /// 把不再属于这个节点的控件摘掉（例如类型从按钮改成了图片）。
        /// 只处理工具会生成的组件，人手加的组件一律保留。
        /// </summary>
        private static void RemoveStaleComponents(PlanNode plan, GameObject go, PrefabBuildContext context)
        {
            // 视觉件换类型了（文字变图片之类），旧的那个必须摘掉，否则两层会叠在一起
            ControlKind visual = ControlFactory.VisualKind(plan);
            if (visual == ControlKind.TmpText && !TmpBackend.Available)
            {
                // 没装 TMP，实际生成的是 uGUI Text
                visual = ControlKind.Text;
            }

            if (visual != ControlKind.Image && go.GetComponent<Image>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Image>(), true);
            }

            if (visual != ControlKind.RawImage && go.GetComponent<RawImage>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<RawImage>(), true);
            }

            if (visual != ControlKind.Text && go.GetComponent<Text>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Text>(), true);
            }

            if (visual != ControlKind.TmpText && TmpBackend.Available && TmpBackend.Current.RemoveText(go))
            {
                context.Report(DiagnosticSeverity.Info, "prefab.tmp-removed",
                    "节点不再是 TMP 文本，已摘掉旧的 TextMeshProUGUI：" + plan.Name);
            }

            if (plan.Kind != ControlKind.Mask && go.GetComponent<Mask>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Mask>(), true);
            }

            if (plan.Kind != ControlKind.Button && go.GetComponent<Button>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Button>(), true);
            }

            if (plan.Kind != ControlKind.Toggle && go.GetComponent<Toggle>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Toggle>(), true);
            }

            if (plan.Kind != ControlKind.Slider && go.GetComponent<Slider>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Slider>(), true);
            }

            if (plan.Kind != ControlKind.Scrollbar && go.GetComponent<Scrollbar>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Scrollbar>(), true);
            }

            if (plan.Kind != ControlKind.ScrollView && plan.Kind != ControlKind.Dropdown &&
                go.GetComponent<ScrollRect>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<ScrollRect>(), true);
            }

            if (plan.Kind != ControlKind.Dropdown && go.GetComponent<Dropdown>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Dropdown>(), true);
            }

            if (plan.Kind != ControlKind.InputField && go.GetComponent<InputField>() != null)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<InputField>(), true);
            }
        }

        private static void Wire(PlanNode plan, GameObject go, Dictionary<string, GameObject> index, string key,
            PrefabBuildContext context)
        {
            switch (plan.Kind)
            {
                case ControlKind.Button:
                    WireButton(plan, go, index, key, context);
                    break;
                case ControlKind.Toggle:
                    WireToggle(plan, go, index, key);
                    break;
                case ControlKind.Slider:
                    WireSlider(plan, go, index, key, context);
                    break;
                case ControlKind.Scrollbar:
                    WireScrollbar(plan, go, index, key, context);
                    break;
                case ControlKind.ScrollView:
                    WireScrollView(plan, go, index, key);
                    break;
                case ControlKind.Dropdown:
                    WireDropdown(plan, go, index, key, context);
                    break;
                case ControlKind.InputField:
                    WireInputField(plan, go, index, key, context);
                    break;
            }
        }

        private static void WireButton(PlanNode plan, GameObject go, Dictionary<string, GameObject> index,
            string key, PrefabBuildContext context)
        {
            var button = GetOrAdd<Button>(go);
            GameObject target = Slot(plan, index, key, PrefabPlanner.SlotTarget);
            Graphic graphic = GraphicOn(target);
            if (graphic == null)
            {
                graphic = go.GetComponent<Graphic>();
            }

            button.targetGraphic = graphic;

            PlanSpriteState state = plan.State;
            if (state != null && !state.IsEmpty)
            {
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = context.Sprite(state.HighlightedId),
                    pressedSprite = context.Sprite(state.PressedId),
                    selectedSprite = context.Sprite(state.SelectedId),
                    disabledSprite = context.Sprite(state.DisabledId)
                };

                var image = target == null ? null : target.GetComponent<Image>();
                if (image != null && image.sprite == null)
                {
                    image.sprite = context.Sprite(state.NormalId);
                    image.color = Color.white;
                }
            }
            else
            {
                // 没有交互态贴图就用颜色过渡，至少手感上是个按钮
                button.transition = Selectable.Transition.ColorTint;
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(0.9608f, 0.9608f, 0.9608f, 1f);
                colors.pressedColor = new Color(0.7843f, 0.7843f, 0.7843f, 1f);
                colors.selectedColor = new Color(0.9608f, 0.9608f, 0.9608f, 1f);
                colors.disabledColor = new Color(0.7843f, 0.7843f, 0.7843f, 0.5019f);
                colors.fadeDuration = 0.1f;
                button.colors = colors;
            }

            if (graphic == null)
            {
                context.Report(DiagnosticSeverity.Warning, "prefab.button-no-target",
                    "按钮没有任何可点的图形（targetGraphic 为空），运行时点不到：" + plan.Name);
            }
        }

        private static void WireToggle(PlanNode plan, GameObject go, Dictionary<string, GameObject> index,
            string key)
        {
            var toggle = GetOrAdd<Toggle>(go);
            toggle.targetGraphic = GraphicOn(Slot(plan, index, key, PrefabPlanner.SlotTarget));
            toggle.graphic = GraphicOn(Slot(plan, index, key, PrefabPlanner.SlotCheckmark));
        }

        private static void WireSlider(PlanNode plan, GameObject go, Dictionary<string, GameObject> index,
            string key, PrefabBuildContext context)
        {
            var slider = GetOrAdd<Slider>(go);
            GameObject fill = Slot(plan, index, key, PrefabPlanner.SlotFill);
            GameObject handle = Slot(plan, index, key, PrefabPlanner.SlotHandle);
            slider.fillRect = RectOf(fill);
            slider.handleRect = HandleRect(plan, go, handle, context);
            slider.targetGraphic = GraphicOn(handle) ?? GraphicOn(fill) ?? go.GetComponent<Graphic>();
            slider.direction = plan.Axis == PlanAxis.Vertical
                ? Slider.Direction.BottomToTop
                : Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;

            var image = fill == null ? null : fill.GetComponent<Image>();
            if (image != null)
            {
                // Slider 靠 fillAmount 驱动，填充图必须是 Filled 类型
                image.type = Image.Type.Filled;
                image.fillMethod = plan.Axis == PlanAxis.Vertical
                    ? Image.FillMethod.Vertical
                    : Image.FillMethod.Horizontal;
                image.fillOrigin = 0;
            }
        }

        private static void WireScrollbar(PlanNode plan, GameObject go, Dictionary<string, GameObject> index,
            string key, PrefabBuildContext context)
        {
            var scrollbar = GetOrAdd<Scrollbar>(go);
            GameObject handle = Slot(plan, index, key, PrefabPlanner.SlotHandle);
            scrollbar.handleRect = HandleRect(plan, go, handle, context);
            scrollbar.targetGraphic = GraphicOn(handle) ?? go.GetComponent<Graphic>();
            scrollbar.direction = plan.Axis == PlanAxis.Vertical
                ? Scrollbar.Direction.BottomToTop
                : Scrollbar.Direction.LeftToRight;
        }

        private static void WireScrollView(PlanNode plan, GameObject go, Dictionary<string, GameObject> index,
            string key)
        {
            var scroll = GetOrAdd<ScrollRect>(go);
            GameObject viewport = Slot(plan, index, key, PrefabPlanner.SlotViewport);
            GameObject content = Slot(plan, index, key, PrefabPlanner.SlotContent);
            scroll.viewport = RectOf(viewport);
            scroll.content = RectOf(content);
            scroll.verticalScrollbar = ComponentOn<Scrollbar>(
                Slot(plan, index, key, PrefabPlanner.SlotVerticalScrollbar));
            scroll.horizontalScrollbar = ComponentOn<Scrollbar>(
                Slot(plan, index, key, PrefabPlanner.SlotHorizontalScrollbar));
            scroll.vertical = scroll.verticalScrollbar != null || scroll.horizontalScrollbar == null;
            scroll.horizontal = scroll.horizontalScrollbar != null;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 20f;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            // 视口必须有遮罩，否则内容会溢出到框外
            if (viewport != null && viewport.GetComponent<Mask>() == null &&
                viewport.GetComponent<RectMask2D>() == null)
            {
                viewport.AddComponent<RectMask2D>();
            }
        }

        private static void WireDropdown(PlanNode plan, GameObject go, Dictionary<string, GameObject> index,
            string key, PrefabBuildContext context)
        {
            var dropdown = GetOrAdd<Dropdown>(go);
            GameObject template = Slot(plan, index, key, PrefabPlanner.SlotTemplate);
            dropdown.template = RectOf(template);
            dropdown.captionText = ComponentOn<Text>(Slot(plan, index, key, PrefabPlanner.SlotCaptionText));
            dropdown.itemText = ComponentOn<Text>(Slot(plan, index, key, PrefabPlanner.SlotItemText));
            dropdown.targetGraphic = go.GetComponent<Graphic>();

            if (dropdown.template == null)
            {
                context.Report(DiagnosticSeverity.Error, "prefab.dropdown-template-missing",
                    "下拉框缺少 Template，运行时展开会报错：" + plan.Name);
            }
        }

        private static void WireInputField(PlanNode plan, GameObject go, Dictionary<string, GameObject> index,
            string key, PrefabBuildContext context)
        {
            var field = GetOrAdd<InputField>(go);
            GameObject text = Slot(plan, index, key, PrefabPlanner.SlotTextComponent);
            field.textComponent = ComponentOn<Text>(text);
            field.placeholder = GraphicOn(Slot(plan, index, key, PrefabPlanner.SlotPlaceholder));
            field.targetGraphic = go.GetComponent<Graphic>();
            field.lineType = InputField.LineType.SingleLine;

            if (field.textComponent == null)
            {
                context.Report(DiagnosticSeverity.Error, "prefab.inputfield-text-missing",
                    "输入框缺少文本组件，运行时无法输入：" + plan.Name);
            }
        }

        /// <summary>
        /// 滑块矩形：设计稿没画滑块时退化成「整条就是滑块」，
        /// 否则 uGUI 的 Slider/Scrollbar 会挂着一个空的 handleRect，拖动没有反馈。
        /// </summary>
        private static RectTransform HandleRect(PlanNode plan, GameObject go, GameObject handle,
            PrefabBuildContext context)
        {
            RectTransform rect = RectOf(handle);
            if (rect != null)
            {
                return rect;
            }

            context.Report(DiagnosticSeverity.Info, "prefab.handle-fallback",
                "没找到滑块图层，已用控件自身矩形当滑块：" + plan.Name);
            return (RectTransform)go.transform;
        }

        private static GameObject Slot(PlanNode plan, Dictionary<string, GameObject> index, string key,
            string slot)
        {
            PlanNode node;
            if (!plan.Slots.TryGetValue(slot, out node))
            {
                return null;
            }

            GameObject go;
            return index.TryGetValue(PlanKeys.Join(key, PlanKeys.Key(node)), out go) ? go : null;
        }

        /// <summary>取组件，没有才加：增量更新会重复走同一段装配代码。</summary>
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static RectTransform RectOf(GameObject go)
        {
            return go == null ? null : go.GetComponent<RectTransform>();
        }

        private static Graphic GraphicOn(GameObject go)
        {
            return go == null ? null : go.GetComponent<Graphic>();
        }

        private static T ComponentOn<T>(GameObject go) where T : Component
        {
            return go == null ? null : go.GetComponent<T>();
        }
    }
}
