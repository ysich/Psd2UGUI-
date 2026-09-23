using System;
using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
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

            var index = new Dictionary<PlanNode, GameObject>();
            GameObject root = CreateNode(plan, null, context, index, true);
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
                string folder = Path.GetDirectoryName(prefabPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Import.Psd2UguiPaths.EnsureAssetFolder(folder.Replace('\\', '/'));
                }

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

        private static GameObject CreateNode(PlanNode plan, Transform parent, PrefabBuildContext context,
            Dictionary<PlanNode, GameObject> index, bool isRoot)
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
            index[plan] = go;
            context.NodesCreated++;

            for (int i = 0; i < plan.Children.Count; i++)
            {
                CreateNode(plan.Children[i], go.transform, context, index, false);
            }

            Wire(plan, go, index, context);
            AttachMarker(plan, go, context);

            if (!plan.Active)
            {
                go.SetActive(false);
            }

            return go;
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

            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = rect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(
                context.Build.ReferenceWidth > 0 ? context.Build.ReferenceWidth : (float)plan.Rect.Width,
                context.Build.ReferenceHeight > 0 ? context.Build.ReferenceHeight : (float)plan.Rect.Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            rect.gameObject.AddComponent<GraphicRaycaster>();
        }

        private static void AttachMarker(PlanNode plan, GameObject go, PrefabBuildContext context)
        {
            if (!context.Build.MarkNodes || string.IsNullOrEmpty(plan.SourceNodeId))
            {
                return;
            }

            var marker = go.AddComponent<Psd2UguiNode>();
            marker.NodeId = plan.SourceNodeId;
            marker.LayerPath = plan.SourceLayerPath;
            marker.LayerId = plan.SourceLayerId;
            marker.Role = plan.Role;
            marker.SourcePsd = context.SourcePsd;
        }

        private static void Wire(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index,
            PrefabBuildContext context)
        {
            switch (plan.Kind)
            {
                case ControlKind.Button:
                    WireButton(plan, go, index, context);
                    break;
                case ControlKind.Toggle:
                    WireToggle(plan, go, index);
                    break;
                case ControlKind.Slider:
                    WireSlider(plan, go, index, context);
                    break;
                case ControlKind.Scrollbar:
                    WireScrollbar(plan, go, index, context);
                    break;
                case ControlKind.ScrollView:
                    WireScrollView(plan, go, index);
                    break;
                case ControlKind.Dropdown:
                    WireDropdown(plan, go, index, context);
                    break;
                case ControlKind.InputField:
                    WireInputField(plan, go, index, context);
                    break;
            }
        }

        private static void WireButton(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index,
            PrefabBuildContext context)
        {
            var button = go.AddComponent<Button>();
            GameObject target = Slot(plan, index, PrefabPlanner.SlotTarget);
            Graphic graphic = GraphicOn(target) ?? go.GetComponent<Graphic>();
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

        private static void WireToggle(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index)
        {
            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = GraphicOn(Slot(plan, index, PrefabPlanner.SlotTarget));
            toggle.graphic = GraphicOn(Slot(plan, index, PrefabPlanner.SlotCheckmark));
        }

        private static void WireSlider(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index,
            PrefabBuildContext context)
        {
            var slider = go.AddComponent<Slider>();
            GameObject fill = Slot(plan, index, PrefabPlanner.SlotFill);
            GameObject handle = Slot(plan, index, PrefabPlanner.SlotHandle);
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

        private static void WireScrollbar(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index,
            PrefabBuildContext context)
        {
            var scrollbar = go.AddComponent<Scrollbar>();
            GameObject handle = Slot(plan, index, PrefabPlanner.SlotHandle);
            scrollbar.handleRect = HandleRect(plan, go, handle, context);
            scrollbar.targetGraphic = GraphicOn(handle) ?? go.GetComponent<Graphic>();
            scrollbar.direction = plan.Axis == PlanAxis.Vertical
                ? Scrollbar.Direction.BottomToTop
                : Scrollbar.Direction.LeftToRight;
        }

        private static void WireScrollView(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index)
        {
            var scroll = go.AddComponent<ScrollRect>();
            GameObject viewport = Slot(plan, index, PrefabPlanner.SlotViewport);
            GameObject content = Slot(plan, index, PrefabPlanner.SlotContent);
            scroll.viewport = RectOf(viewport);
            scroll.content = RectOf(content);
            scroll.verticalScrollbar = ComponentOn<Scrollbar>(Slot(plan, index, PrefabPlanner.SlotVerticalScrollbar));
            scroll.horizontalScrollbar = ComponentOn<Scrollbar>(Slot(plan, index, PrefabPlanner.SlotHorizontalScrollbar));
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

        private static void WireDropdown(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index,
            PrefabBuildContext context)
        {
            var dropdown = go.AddComponent<Dropdown>();
            GameObject template = Slot(plan, index, PrefabPlanner.SlotTemplate);
            dropdown.template = RectOf(template);
            dropdown.captionText = ComponentOn<Text>(Slot(plan, index, PrefabPlanner.SlotCaptionText));
            dropdown.itemText = ComponentOn<Text>(Slot(plan, index, PrefabPlanner.SlotItemText));
            dropdown.targetGraphic = go.GetComponent<Graphic>();

            if (dropdown.template == null)
            {
                context.Report(DiagnosticSeverity.Error, "prefab.dropdown-template-missing",
                    "下拉框缺少 Template，运行时展开会报错：" + plan.Name);
            }
        }

        private static void WireInputField(PlanNode plan, GameObject go, Dictionary<PlanNode, GameObject> index,
            PrefabBuildContext context)
        {
            var field = go.AddComponent<InputField>();
            GameObject text = Slot(plan, index, PrefabPlanner.SlotTextComponent);
            field.textComponent = ComponentOn<Text>(text);
            field.placeholder = GraphicOn(Slot(plan, index, PrefabPlanner.SlotPlaceholder));
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

        private static GameObject Slot(PlanNode plan, Dictionary<PlanNode, GameObject> index, string slot)
        {
            PlanNode node;
            GameObject go;
            return plan.Slots.TryGetValue(slot, out node) && index.TryGetValue(node, out go) ? go : null;
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
