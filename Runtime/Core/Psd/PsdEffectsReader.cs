using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Psd
{
    /// <summary>
    /// lfx2（对象化图层效果）读取。
    /// 效果类型由值描述符的 classID 决定（键名在不同 PS 版本里可能是 DrSh 或 dropShadowMulti），
    /// 且只有 enab / present 都为真的条目才算生效——与 Photoshop 面板的显示一致。
    /// </summary>

    public static class PsdEffectsReader
    {
        public static List<UiEffect> Parse(PsdDescriptor descriptor, IList<string> unknown)
        {
            var effects = new List<UiEffect>();
            if (descriptor == null)
            {
                return effects;
            }

            for (int i = 0; i < descriptor.Items.Count; i++)
            {
                PsdValue value = descriptor.Items[i].Value;
                if (value == null)
                {
                    continue;
                }

                if (value.IsList)
                {
                    for (int j = 0; j < value.Items.Count; j++)
                    {
                        Collect(value.Items[j], effects, unknown);
                    }

                    continue;
                }

                Collect(value, effects, unknown);
            }

            return effects;
        }

        private static void Collect(PsdValue value, List<UiEffect> effects, IList<string> unknown)
        {
            PsdDescriptor effect = value == null ? null : value.Object;
            if (effect == null)
            {
                return;
            }

            string kind = MapKind(effect.ClassId);
            if (kind == null)
            {
                if (unknown != null && !string.IsNullOrEmpty(effect.ClassId) &&
                    effect.ClassId != "Scl " && effect.ClassId != "numM")
                {
                    unknown.Add(effect.ClassId);
                }

                return;
            }

            // enab 才是图层样式面板上的开关：有的图层会留下 present=true 但已被关闭的旧样式，
            // 只看 present 会把这些关闭的样式也导出去。
            if (effect.Has("present") && !effect.GetBool("present", true))
            {
                return;
            }

            if (effect.Has("enab") && !effect.GetBool("enab", true))
            {
                return;
            }

            effects.Add(BuildEffect(kind, effect));

        }

        private static string MapKind(string classId)
        {
            switch (classId)
            {
                case "DrSh":
                    return "drop-shadow";
                case "IrSh":
                    return "inner-shadow";
                case "OrGl":
                    return "outer-glow";
                case "IrGl":
                    return "inner-glow";
                case "ebbl":
                    return "bevel";
                case "SoFi":
                    return "solid-fill";
                case "GrFl":
                    return "gradient-fill";
                case "FrFX":
                    return "stroke";
                case "ChFX":
                    return "satin";
                case "patternFill":
                    return "pattern-fill";
                default:
                    return null;
            }
        }

        private static UiEffect BuildEffect(string kind, PsdDescriptor descriptor)
        {
            var effect = new UiEffect { Kind = kind };
            effect.Enabled = descriptor.GetBool("enab", true);
            effect.BlendMode = descriptor.GetEnum("Md ", string.Empty);
            effect.Size = descriptor.GetNumber("blur", 0d);
            if (effect.Size <= 0d)
            {
                effect.Size = descriptor.GetNumber("Sz  ", 0d);
            }

            effect.Distance = descriptor.GetNumber("Dstn", 0d);
            effect.Angle = descriptor.GetNumber("lagl", 0d);
            effect.Choke = descriptor.GetNumber("Ckmt", 0d);
            effect.Color = descriptor.GetColor("Clr ", UiColor.White);
            effect.Opacity = descriptor.GetNumber("Opct", 100d) / 100d;

            string style = descriptor.GetEnum("Styl", null);
            if (style == "OutF")
            {
                effect.Style = "outside";
            }
            else if (style == "InsF")
            {
                effect.Style = "inside";
            }
            else if (style == "CtrF")
            {
                effect.Style = "center";
            }

            string glowStyle = descriptor.GetEnum("GlwT", null);
            if (glowStyle == "SrgB")
            {
                effect.Style = "precise";
            }
            else if (glowStyle == "Nrml")
            {
                effect.Style = "softer";
            }

            PsdDescriptor highlight = descriptor.GetObject("hglC");
            if (highlight != null)
            {
                effect.SecondColor = highlight.ToColor(UiColor.White);
                effect.HasSecondColor = true;
            }

            if (!effect.HasSecondColor)
            {
                UiColor start;
                UiColor end;
                if (TryReadGradient(descriptor.GetObject("Grad"), out start, out end))
                {
                    effect.Color = start;
                    effect.SecondColor = end;
                    effect.HasSecondColor = true;
                }
            }

            return effect;
        }

        private static bool TryReadGradient(PsdDescriptor gradient, out UiColor start, out UiColor end)
        {
            start = UiColor.White;
            end = UiColor.White;
            if (gradient == null)
            {
                return false;
            }

            List<PsdValue> colors = gradient.GetList("Clrs");
            if (colors == null)
            {
                return false;
            }

            bool hasStart = false;
            bool hasEnd = false;
            for (int i = 0; i < colors.Count; i++)
            {
                PsdDescriptor stop = colors[i].Object;
                if (stop == null)
                {
                    continue;
                }

                UiColor color = stop.ToColor(UiColor.White);
                if (!hasStart)
                {
                    start = color;
                    hasStart = true;
                }

                end = color;
                hasEnd = true;
            }

            return hasStart && hasEnd;
        }
    }
}
