using System.Globalization;
using System.Text;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Build
{
    /// <summary>
    /// 计划节点的「受管属性指纹」。
    ///
    /// 生成时把它写进 <c>Psd2UguiNode</c>，重新导出时拿来比对：
    /// 指纹一样说明这个节点对应的 PSD 内容没变，工具就完全不动它，
    /// 人手调过的位置、颜色、字体因此都能留住；指纹变了才按新值覆盖。
    /// </summary>
    public static class PlanSpec
    {
        public static string Hash(PlanNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(160);
            builder.Append(node.Kind).Append('|');
            AppendRect(builder, node.Rect);
            if (node.DrawRect == null)
            {
                // 没裁过内容框的节点保持原来的指纹，重新导出时不会被无谓地刷一遍
                builder.Append('-');
            }
            else
            {
                AppendRect(builder, node.DrawRect.Value);
            }

            builder.Append(node.Anchor == null ? "-" : AnchorKey(node.Anchor)).Append('|');
            builder.Append(node.Active ? '1' : '0').Append('|');
            builder.Append(Number(node.Opacity)).Append('|');
            builder.Append(node.SpriteId ?? "-").Append('|');
            builder.Append(node.Sliced ? BorderKey(node.Border) : "-").Append('|');
            builder.Append(node.HasColor ? node.Color.ToHexRgba() : "-").Append('|');
            builder.Append(node.PrefabTarget ?? "-").Append('|');
            AppendText(builder, node.Text);
            AppendEffects(builder, node.Effects);
            return StableId.Hash(builder.ToString());
        }

        private static void AppendRect(StringBuilder builder, UiRect rect)
        {
            builder.Append(Number(rect.X)).Append(',')
                .Append(Number(rect.Y)).Append(',')
                .Append(Number(rect.Width)).Append(',')
                .Append(Number(rect.Height));
        }

        private static string AnchorKey(PlanAnchor anchor)
        {
            return Number(anchor.MinX) + "," + Number(anchor.MinY) + "," + Number(anchor.MaxX) + "," +
                   Number(anchor.MaxY) + "," + Number(anchor.Left) + "," + Number(anchor.Bottom) + "," +
                   Number(anchor.Right) + "," + Number(anchor.Top);
        }

        private static string BorderKey(UiBorder border)
        {
            return border == null
                ? "-"
                : border.Left + "," + border.Bottom + "," + border.Right + "," + border.Top;
        }

        private static void AppendText(StringBuilder builder, UiTextInfo text)
        {
            if (text == null || !text.HasValue)
            {
                builder.Append("-|");
                return;
            }

            builder.Append(text.Content).Append('|')
                .Append(Number(text.FontSize)).Append('|')
                .Append(text.Align).Append('|')
                .Append(text.FontName).Append('|')
                .Append(text.Color.ToHexRgba()).Append('|')
                .Append(text.WordWrap ? '1' : '0').Append('|');
        }

        private static void AppendEffects(StringBuilder builder, System.Collections.Generic.List<UiEffect> effects)
        {
            if (effects == null || effects.Count == 0)
            {
                builder.Append('-');
                return;
            }

            for (int i = 0; i < effects.Count; i++)
            {
                UiEffect effect = effects[i];
                if (!effect.Enabled)
                {
                    continue;
                }

                builder.Append(effect.Kind).Append(':')
                    .Append(Number(effect.Size)).Append(':')
                    .Append(Number(effect.Distance)).Append(':')
                    .Append(Number(effect.Angle)).Append(':')
                    .Append(effect.Color.ToHexRgba()).Append(';');
            }
        }

        private static string Number(double value)
        {
            // 用固定的最短表示，保证同一份数据每次算出的指纹一样
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
