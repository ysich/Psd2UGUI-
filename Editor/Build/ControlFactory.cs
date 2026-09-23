using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using UnityEngine;
using UnityEngine.UI;

namespace Psd2Ugui.Editor.Build
{
    /// <summary>契约里的颜色/对齐/九宫 → Unity 组件属性的换算。</summary>
    public static class UiConvert
    {
        public static Color ToColor(this UiColor color)
        {
            return new Color((float)color.R, (float)color.G, (float)color.B, (float)color.A);
        }

        public static TextAnchor ToAnchor(string align)
        {
            switch ((align ?? "center").ToLowerInvariant())
            {
                case "left":
                    return TextAnchor.MiddleLeft;
                case "right":
                    return TextAnchor.MiddleRight;
                default:
                    return TextAnchor.MiddleCenter;
            }
        }

        public static bool IsLeft(string align)
        {
            return string.Equals(align, "left", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 计划节点 → 组件。只负责「一个 GameObject 上挂什么、属性怎么设」，
    /// 连线用到的子对象由 <see cref="PrefabBuilder"/> 在子树建好之后传进来。
    /// </summary>
    public static class ControlFactory
    {
        /// <summary>按计划放置 RectTransform。</summary>
        public static void ApplyLayout(RectTransform rect, PlanNode plan)
        {
            if (plan.Anchor != null)
            {
                PlanAnchor anchor = plan.Anchor;
                rect.anchorMin = new Vector2((float)anchor.MinX, (float)anchor.MinY);
                rect.anchorMax = new Vector2((float)anchor.MaxX, (float)anchor.MaxY);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2((float)anchor.Left, (float)anchor.Bottom);
                rect.offsetMax = new Vector2((float)-anchor.Right, (float)-anchor.Top);
                return;
            }

            // PSD 是左上角原点、Y 向下；uGUI 用左上锚点 + 负的 Y 偏移就能一一对应
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2((float)plan.Rect.X, (float)-plan.Rect.Y);
            rect.sizeDelta = new Vector2((float)plan.Rect.Width, (float)plan.Rect.Height);
        }

        /// <summary>按计划挂视觉组件（Image / RawImage / Text / TMP / 纯色）。</summary>
        public static void ApplyVisual(PlanNode plan, GameObject target, PrefabBuildContext context)
        {
            switch (plan.Kind)
            {
                case ControlKind.Image:
                case ControlKind.Panel:
                case ControlKind.Mask:
                case ControlKind.FillColor:
                    AddImage(plan, target, context);
                    break;
                case ControlKind.RawImage:
                    AddRawImage(plan, target, context);
                    break;
                case ControlKind.Text:
                    AddText(plan, target, context);
                    break;
                case ControlKind.TmpText:
                    AddTmpText(plan, target, context);
                    break;
                default:
                    // 复合控件本身也可能有底图（例如整个按钮组的像素）
                    if (!string.IsNullOrEmpty(plan.SpriteId))
                    {
                        AddImage(plan, target, context);
                    }

                    break;
            }

            ReportUnappliedEffects(plan, context);

            if (plan.Kind == ControlKind.Rect && plan.Children.Count > 0)
            {
                // 容器自己没颜色可乘，只能用 CanvasGroup 整体压透明度
                var group = target.GetComponent<CanvasGroup>();
                if (plan.Opacity < 0.999d)
                {
                    if (group == null)
                    {
                        group = target.AddComponent<CanvasGroup>();
                    }

                    group.alpha = (float)plan.Opacity;
                }
                else if (group != null)
                {
                    // 透明度调回 1 了，多出来的 CanvasGroup 会挡住射线，摘掉
                    UnityEngine.Object.DestroyImmediate(group, true);
                }
            }
        }

        /// <summary>取组件，没有才加。增量更新会重复走同一条装配路径，不能每次 AddComponent。</summary>
        internal static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        /// <summary>
        /// 这个节点最终该长成哪种「视觉件」。
        /// 复合控件（按钮/开关…）本身也可能带底图，这时也算图片。
        /// </summary>
        public static ControlKind VisualKind(PlanNode plan)
        {
            switch (plan.Kind)
            {
                case ControlKind.Image:
                case ControlKind.Panel:
                case ControlKind.Mask:
                case ControlKind.FillColor:
                    return ControlKind.Image;
                case ControlKind.RawImage:
                    return ControlKind.RawImage;
                case ControlKind.Text:
                    return ControlKind.Text;
                case ControlKind.TmpText:
                    return ControlKind.TmpText;
                default:
                    return string.IsNullOrEmpty(plan.SpriteId) ? ControlKind.Rect : ControlKind.Image;
            }
        }

        private static void AddImage(PlanNode plan, GameObject target, PrefabBuildContext context)
        {
            var image = GetOrAdd<Image>(target);
            image.sprite = context.Sprite(plan.SpriteId);
            image.color = ColorFor(plan, (float)plan.Opacity);
            if (image.sprite != null)
            {
                image.type = plan.Sliced ? Image.Type.Sliced : Image.Type.Simple;
            }

            if (plan.Kind == ControlKind.Mask)
            {
                // 有底图时用 Mask（画笔形状遮罩），没有就退化成矩形遮罩
                if (image.sprite != null)
                {
                    var mask = GetOrAdd<Mask>(target);
                    mask.showMaskGraphic = false;
                }
                else
                {
                    var rectMask = GetOrAdd<RectMask2D>(target);
                    rectMask.enabled = true;
                }
            }
            else if (plan.Kind == ControlKind.Panel && plan.Clipping)
            {
                var rectMask = GetOrAdd<RectMask2D>(target);
                rectMask.enabled = true;
            }
        }

        /// <summary>
        /// 图片节点上的图层效果 uGUI 表达不了（投影、描边、渐变都算），这里明说一句。
        /// 不吭声的话用户只会觉得「效果怎么没了」，而这个插件承诺的是「做不到也要说」。
        /// </summary>
        private static void ReportUnappliedEffects(PlanNode plan, PrefabBuildContext context)
        {
            if (plan.Effects == null || plan.Effects.Count == 0)
            {
                return;
            }

            if (plan.Kind == ControlKind.Text || plan.Kind == ControlKind.TmpText)
            {
                // 文本有自己的通道（Outline / Shadow / TMP 渐变），交给 ApplyTextEffects 处理
                return;
            }

            var kinds = new List<string>();
            for (int i = 0; i < plan.Effects.Count; i++)
            {
                UiEffect effect = plan.Effects[i];
                if (effect.Enabled && !kinds.Contains(effect.Kind))
                {
                    kinds.Add(effect.Kind);
                }
            }

            if (kinds.Count == 0)
            {
                return;
            }

            context.Report(DiagnosticSeverity.Info, "effect.not-applied",
                "图层效果没法用 uGUI 表达，已忽略：" + plan.Name + "（" + string.Join("、", kinds.ToArray()) + "）");
        }

        private static void AddRawImage(PlanNode plan, GameObject target, PrefabBuildContext context)
        {
            var image = GetOrAdd<RawImage>(target);
            Sprite sprite = context.Sprite(plan.SpriteId);
            if (sprite != null)
            {
                image.texture = sprite.texture;
            }

            image.color = ColorFor(plan, (float)plan.Opacity);
        }

        private static void AddText(PlanNode plan, GameObject target, PrefabBuildContext context)
        {
            UiTextInfo info = plan.Text;
            var text = GetOrAdd<Text>(target);
            text.text = info == null ? plan.Name : info.Content;
            text.font = context.Font(info == null ? null : info.FontName);
            text.fontSize = FontSize(info);
            text.color = TextColor(plan, (float)plan.Opacity);
            text.alignment = UiConvert.ToAnchor(info == null ? "center" : info.Align);
            text.horizontalOverflow = info != null && info.WordWrap
                ? HorizontalWrapMode.Wrap
                : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            ApplyTextEffects(plan, target, context, false);
        }

        private static void AddTmpText(PlanNode plan, GameObject target, PrefabBuildContext context)
        {
            UiTextInfo info = plan.Text;
            Graphic graphic = TmpBackend.Available
                ? TmpBackend.Current.AddText(target, info, plan.Opacity)
                : null;
            if (graphic == null)
            {
                context.Report(DiagnosticSeverity.Warning, "text.tmp-unavailable",
                    "没有检测到 TextMeshPro，已改用 uGUI Text：" + plan.Name);
                AddText(plan, target, context);
                return;
            }

            ApplyTextEffects(plan, target, context, true);
        }

        private static int FontSize(UiTextInfo info)
        {
            double size = info == null ? 0d : info.FontSize;
            return Mathf.Max(1, Mathf.RoundToInt((float)size));
        }

        /// <summary>文本颜色：PSD 的文字颜色，被 solid-fill 效果覆盖时以效果为准。</summary>
        public static Color TextColor(PlanNode plan, float opacity)
        {
            UiColor color = plan.Text == null ? UiColor.White : plan.Text.Color;
            if (plan.Effects != null)
            {
                for (int i = 0; i < plan.Effects.Count; i++)
                {
                    UiEffect effect = plan.Effects[i];
                    if (effect.Enabled && effect.Kind == "solid-fill")
                    {
                        color = effect.Color;
                    }
                }
            }

            Color result = color.ToColor();
            result.a *= opacity;
            return result;
        }

        private static Color ColorFor(PlanNode plan, float opacity)
        {
            Color color = plan.HasColor ? plan.Color.ToColor() : Color.white;
            color.a *= opacity;
            return color;
        }

        /// <summary>
        /// 文字效果：描边/外发光用 Outline，投影用 Shadow，
        /// uGUI 做不了的（内阴影、渐变、图案）只给诊断，绝不悄悄丢。
        /// </summary>
        public static void ApplyTextEffects(PlanNode plan, GameObject target, PrefabBuildContext context, bool isTmp)
        {
            if (!context.Build.ApplyEffects || plan.Effects == null || plan.Effects.Count == 0)
            {
                return;
            }

            for (int i = 0; i < plan.Effects.Count; i++)
            {
                UiEffect effect = plan.Effects[i];
                if (!effect.Enabled)
                {
                    continue;
                }

                switch (effect.Kind)
                {
                    case "stroke":
                    case "outer-glow":
                        AddOutline(plan, target, effect);
                        break;
                    case "drop-shadow":
                        AddShadow(plan, target, effect);
                        break;
                    case "solid-fill":
                        break;
                    case "gradient-fill":
                        if (!isTmp)
                        {
                            context.Report(DiagnosticSeverity.Info, "text.gradient-unsupported",
                                "uGUI Text 不支持渐变填充，已忽略该效果：" + plan.Name);
                        }
                        else if (!TmpBackend.Current.ApplyGradient(target, effect))
                        {
                            context.Report(DiagnosticSeverity.Info, "text.gradient-unsupported",
                                "渐变填充没能套到 TMP 上（需要在 TMP 材质里开启顶点渐变）：" + plan.Name);
                        }

                        break;
                    default:
                        context.Report(DiagnosticSeverity.Info, "text.effect-unsupported",
                            "uGUI 无法表达文字效果「" + effect.Kind + "」，已忽略：" + plan.Name);
                        break;
                }
            }
        }

        private static void AddOutline(PlanNode plan, GameObject target, UiEffect effect)
        {
            var outline = target.GetComponent<Outline>();
            if (outline == null)
            {
                outline = target.AddComponent<Outline>();
            }

            outline.effectColor = effect.Color.ToColor();
            outline.effectDistance = new Vector2(
                Mathf.Max(1f, (float)effect.Size),
                -Mathf.Max(1f, (float)effect.Size));
        }

        private static void AddShadow(PlanNode plan, GameObject target, UiEffect effect)
        {
            var shadow = target.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = target.AddComponent<Shadow>();
            }

            double angle = effect.Angle * Math.PI / 180d;
            shadow.effectColor = effect.Color.ToColor();
            shadow.effectDistance = new Vector2(
                (float)(Math.Cos(angle) * effect.Distance),
                (float)(Math.Sin(angle) * effect.Distance));
        }
    }
}
