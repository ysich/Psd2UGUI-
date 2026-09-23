using System.Collections.Generic;
using System.Text;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Psd
{
    /// <summary>从 TySh 描述符里提取出来的文本信息。</summary>
    public sealed class PsdTextEngineInfo
    {
        public bool HasValue;
        public string Text = string.Empty;
        public double FontSize;
        public string FontName = string.Empty;
        public UiColor Color = UiColor.White;
        public bool HasColor;
        public UiColor StrokeColor = UiColor.White;
        public bool HasStrokeColor;

        public double Tracking;
        public double Leading;
        public double AutoLeading;
        public string Justification = string.Empty;
        public double TransformXX = 1d;
        public double TransformYY = 1d;
        public double TransformX;
        public double TransformY;
        public EngineDataNode EngineData;

        public static PsdTextEngineInfo FromDescriptor(PsdDescriptor tySh)
        {
            var info = new PsdTextEngineInfo();
            if (tySh == null)
            {
                return info;
            }

            info.HasValue = true;
            info.Text = NormalizeText(tySh.GetText("Txt "));

            PsdValue engine = tySh.Get("EngineData");
            if (engine != null && engine.Data != null && engine.Data.Length > 0)
            {
                info.EngineData = EngineDataParser.Parse(engine.Data, 0, engine.Data.Length);
                ApplyEngineData(info, info.EngineData);
            }

            if (string.IsNullOrEmpty(info.Text) && info.EngineData != null)
            {
                EngineDataNode editor = info.EngineData.Path("EngineDict", "Editor", "Text");
                if (editor != null)
                {
                    info.Text = NormalizeText(editor.AsString());
                }
            }

            return info;
        }

        /// <summary>去掉结尾的终止符，并把 PS 的 \r 换行统一成 \n。</summary>
        public static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.TrimEnd('\0', '\r', '\n').Replace("\r", "\n");
        }

        private static void ApplyEngineData(PsdTextEngineInfo info, EngineDataNode engine)
        {
            EngineDataNode runs = engine.Path("EngineDict", "StyleRun", "RunArray");
            EngineDataNode firstRun = runs == null ? null : runs.At(0);
            EngineDataNode sheet = firstRun == null ? null : firstRun.Get("StyleSheet");

            // 真正的样式字段在 StyleSheet 下面一层 StyleSheetData 里，
            // 个别写入器会省略这一层，所以缺失时退回上层节点。
            EngineDataNode style = sheet == null ? null : sheet.Get("StyleSheetData");
            if (style == null)
            {
                style = sheet;
            }

            if (style != null)
            {
                info.FontSize = style.GetNumber("FontSize", info.FontSize);
                info.Tracking = style.GetNumber("Tracking", info.Tracking);
                info.Leading = style.GetNumber("Leading", info.Leading);
                info.AutoLeading = style.GetNumber("AutoLeading", info.AutoLeading);

                UiColor fill;
                if (TryReadEngineColor(style.Get("FillColor"), out fill))
                {
                    info.Color = fill;
                    info.HasColor = true;
                }

                UiColor stroke;
                if (TryReadEngineColor(style.Get("StrokeColor"), out stroke))
                {
                    info.StrokeColor = stroke;
                    info.HasStrokeColor = true;
                }

                EngineDataNode font = style.Get("Font");
                if (font != null)
                {
                    int fontIndex = (int)font.AsNumber(-1d);
                    info.FontName = ResolveFontName(engine, fontIndex);
                }
            }

            EngineDataNode paragraphs = engine.Path("EngineDict", "ParagraphRun", "RunArray");
            EngineDataNode firstParagraph = paragraphs == null ? null : paragraphs.At(0);
            EngineDataNode paragraphSheet = firstParagraph == null ? null : firstParagraph.Get("ParagraphSheet");

            // 段落属性同样多包了一层 Properties
            EngineDataNode properties = paragraphSheet == null ? null : paragraphSheet.Get("Properties");
            if (properties == null)
            {
                properties = paragraphSheet;
            }

            info.Justification = properties == null
                ? "left"
                : MapJustification(properties.GetNumber("Justification", 0d));
        }

        /// <summary>
        /// EngineData 的颜色写成 <c>&lt;&lt; /Type 1 /Values [ A R G B ] &gt;&gt;</c>，
        /// 分量是 0~1 的浮点数；有的写入器会省略外层字典只剩数组。
        /// </summary>
        private static bool TryReadEngineColor(EngineDataNode node, out UiColor color)
        {
            color = UiColor.White;
            if (node == null)
            {
                return false;
            }

            EngineDataNode values = node.IsDict ? node.Get("Values") : node;
            if (values == null || !values.IsArray || values.Array.Count < 3)
            {
                return false;
            }

            if (values.Array.Count >= 4)
            {
                color = new UiColor(
                    Clamp01(NumberAt(values, 1)),
                    Clamp01(NumberAt(values, 2)),
                    Clamp01(NumberAt(values, 3)),
                    Clamp01(NumberAt(values, 0, 1d)));
            }
            else
            {
                color = new UiColor(
                    Clamp01(NumberAt(values, 0)),
                    Clamp01(NumberAt(values, 1)),
                    Clamp01(NumberAt(values, 2)),
                    1d);
            }

            return true;
        }

        private static double NumberAt(EngineDataNode array, int index, double fallback = 0d)
        {
            EngineDataNode item = array == null ? null : array.At(index);
            return item == null ? fallback : item.AsNumber(fallback);
        }

        private static double Clamp01(double value)
        {
            if (value < 0d)
            {
                return 0d;
            }

            return value > 1d ? 1d : value;
        }


        private static string ResolveFontName(EngineDataNode engine, int fontIndex)
        {
            // FontSet 挂在 EngineDict 的兄弟节点 ResourceDict 下，
            // 个别文档会把它塞进 EngineDict 里，这里两种都试。
            EngineDataNode fontSet = engine.Path("ResourceDict", "FontSet");
            if (fontSet == null)
            {
                fontSet = engine.Path("EngineDict", "ResourceDict", "FontSet");
            }

            if (fontSet == null || !fontSet.IsArray || fontSet.Array.Count == 0)
            {
                return string.Empty;
            }

            EngineDataNode entry = fontSet.At(fontIndex < 0 ? 0 : fontIndex);

            return entry == null ? string.Empty : entry.GetString("Name");
        }

        private static string MapJustification(double value)
        {
            switch ((int)value)
            {
                case 1:
                    return "right";
                case 2:
                    return "center";
                default:
                    return "left";
            }
        }

        /// <summary>把 lsct 分隔符类型转成字符串。</summary>
        public static string DescribeDivider(int type)
        {
            switch (type)
            {
                case PsdSectionDivider.OpenFolder:
                    return "open";
                case PsdSectionDivider.ClosedFolder:
                    return "closed";
                case PsdSectionDivider.BoundingSectionDivider:
                    return "bounding";
                default:
                    return string.Empty;
            }
        }

        /// <summary>描述符遍历，供诊断输出使用。</summary>
        public static IEnumerable<string> DescribeKeys(PsdDescriptor descriptor)
        {
            if (descriptor == null)
            {
                yield break;
            }

            for (int i = 0; i < descriptor.Items.Count; i++)
            {
                yield return descriptor.Items[i].Key;
            }
        }
    }
}
