using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Pipeline
{
    /// <summary>预检的可调项。这一层只产出诊断，不改任何数据。</summary>
    public sealed class PreflightOptions
    {
        public bool Enabled = true;

        /// <summary>
        /// 工程里可用的字体名（Editor 侧从 AssetDatabase 填）。
        /// 留空表示不做字体检查——Core 不该假装自己知道工程里有什么字体。
        /// </summary>
        public List<string> KnownFonts;

        /// <summary>超过这个边长就提醒会掉精度。</summary>
        public int MaxTextureSize = 4096;

        /// <summary>是否提示「类型是靠名字猜出来的」。</summary>
        public bool ReportInferredControls = true;
    }

    /// <summary>
    /// 交付前的预检。
    ///
    /// 目标是「宁可多说一句，也不要在用户拿到预制体之后才发现问题」：
    /// 这里查的都是后面几步会真的出问题、但默认流程不会报错的情况。
    /// 诊断码统一用 `preflight.` 前缀，方便按来源过滤。
    /// </summary>
    public static class Preflight
    {
        /// <summary>返回本次新增的诊断条数。</summary>
        public static int Run(UiDocument document, ExportPlan plan, PreflightOptions options = null)
        {
            options = options ?? new PreflightOptions();
            if (!options.Enabled || document == null)
            {
                return 0;
            }

            int before = document.Diagnostics.Count;
            CheckCanvas(document);
            CheckDuplicateNames(document, plan);
            CheckSize(document, plan, options);
            CheckSlices(document, plan, options);
            CheckTexts(document, options);
            Walk(document, options);
            return document.Diagnostics.Count - before;
        }

        private static void CheckCanvas(UiDocument document)
        {
            PsdMeta meta = document.Document;
            if (meta.Width <= 0 || meta.Height <= 0)
            {
                document.Report(DiagnosticSeverity.Error, "preflight.empty-canvas",
                    "画布尺寸非法（" + meta.Width + "x" + meta.Height + "），无法生成预制体");
                return;
            }

            if (document.Root == null || document.Root.Children.Count == 0)
            {
                document.Report(DiagnosticSeverity.Warning, "preflight.empty-document",
                    "画布里没有任何可见图层，生成出来的预制体会是空的");
            }
        }

        /// <summary>重名资源：`ref &lt;名字&gt;` 就靠名字找人，重名必然产生歧义。</summary>
        private static void CheckDuplicateNames(UiDocument document, ExportPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            var byName = new Dictionary<string, List<SpriteExport>>();
            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                SpriteExport sprite = plan.Sprites[i];
                List<SpriteExport> list;
                if (!byName.TryGetValue(sprite.Name, out list))
                {
                    list = new List<SpriteExport>();
                    byName[sprite.Name] = list;
                }

                list.Add(sprite);
            }

            var names = new List<string>(byName.Keys);
            names.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                List<SpriteExport> list = byName[names[i]];
                string key = list[0].Key;
                bool sameContent = true;
                for (int j = 1; j < list.Count; j++)
                {
                    if (list[j].Key != key)
                    {
                        sameContent = false;
                        break;
                    }
                }

                if (list.Count > 1 && !sameContent)
                {
                    document.Report(DiagnosticSeverity.Warning, "preflight.duplicate-sprite-name",
                        "有 " + list.Count + " 个不同的图层都叫「" + names[i] +
                        "」，`ref " + names[i] + "` 会取图层顺序里的第一张；建议改名或改用覆盖表指定");
                }
            }
        }

        /// <summary>
        /// 超尺寸图：贴图边长超过工程的 Max Size 时 Unity 会静默缩小，
        /// 画家看到的和运行时的不一样，属于必须提前说清楚的事。
        /// </summary>
        private static void CheckSize(UiDocument document, ExportPlan plan, PreflightOptions options)
        {
            if (plan == null || options.MaxTextureSize <= 0)
            {
                return;
            }

            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                SpriteExport sprite = plan.Sprites[i];
                if (sprite.Bitmap == null)
                {
                    continue;
                }

                int width = sprite.Bitmap.Width;
                int height = sprite.Bitmap.Height;
                if (width <= options.MaxTextureSize && height <= options.MaxTextureSize)
                {
                    continue;
                }

                document.Report(DiagnosticSeverity.Warning, "preflight.oversized-sprite",
                    "「" + sprite.Name + "」是 " + width + "x" + height + "，超过工程的贴图上限 " +
                    options.MaxTextureSize + "，导入时会被缩小；" +
                    "把图切小，或者在设计稿阶段就把大图拆成几张");
            }
        }

        /// <summary>
        /// 九宫歧义：边框把中间的可拉伸区切没了。
        ///
        /// 注意「中间只剩 1 像素」是常见且正确的写法（1 像素拉伸的描边块），
        /// 所以这里只在中间区**完全消失**时才报警，否则真实工程会全是噪音。
        /// 设计稿标了 sliced 但没检测出九宫的情况由 Step 5 的 `nine-slice.failed` 负责。
        /// </summary>
        private static void CheckSlices(UiDocument document, ExportPlan plan, PreflightOptions options)
        {
            if (plan == null)
            {
                return;
            }

            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                SpriteExport sprite = plan.Sprites[i];
                if (!sprite.IsSliceable || sprite.Bitmap == null)
                {
                    continue;
                }

                UiBorder border = sprite.Border;
                int width = sprite.Bitmap.Width;
                int height = sprite.Bitmap.Height;
                if (border.Left + border.Right < width && border.Top + border.Bottom < height)
                {
                    continue;
                }

                document.Report(DiagnosticSeverity.Warning, "preflight.slice-degenerate",
                    "「" + sprite.Name + "」被切成了九宫 " + border + "，但图只有 " + width + "x" + height +
                    "，中间已经没有可拉伸的区域；拉伸时会把边框一起拉变形。" +
                    "要么加 `noslice` 标签，要么在设计稿里把边框区域画大一点");
            }
        }

        private static void CheckTexts(UiDocument document, PreflightOptions options)
        {
            if (options.KnownFonts == null)
            {
                return;
            }

            var known = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < options.KnownFonts.Count; i++)
            {
                known.Add(options.KnownFonts[i]);
            }

            foreach (UiNode node in document.Nodes())
            {
                if (node.Text == null || !node.Text.HasValue)
                {
                    continue;
                }

                string font = node.Text.FontName;
                if (string.IsNullOrEmpty(font))
                {
                    document.Report(DiagnosticSeverity.Info, "preflight.font-unknown",
                        "「" + node.Name + "」在设计稿里没有字体信息，生成时会用工程默认字体", node);
                    continue;
                }

                if (!known.Contains(font))
                {
                    document.Report(DiagnosticSeverity.Warning, "preflight.font-missing",
                        "工程里没有字体「" + font + "」（" + node.Name + "），会退回默认字体；" +
                        "把字体装进工程或改名即可消除", node);
                }
            }
        }

        /// <summary>
        /// 逐节点检查：靠名字猜出来的复合控件、被整棵忽略的子树。
        /// 这两种情况默认流程都是「静默生效」的，必须说出来。
        /// </summary>
        private static void Walk(UiDocument document, PreflightOptions options)
        {
            foreach (UiNode node in document.Nodes())
            {
                if (node.Type == UiElementType.Ignore)
                {
                    // CountDescendants 把自己也算进去，这里要的是「会一起被跳过的子图层」
                    int hidden = node.CountDescendants() - 1;
                    if (hidden > 0)
                    {
                        document.Report(DiagnosticSeverity.Warning, "preflight.ignored-subtree",
                            "「" + node.Name + "」被标了 ignore，它下面的 " + hidden +
                            " 个子图层会一起被跳过；不想要这样的结果就删掉 ignore", node);
                    }

                    continue;
                }

                if (!options.ReportInferredControls || !IsInferred(node))
                {
                    continue;
                }

                if (HasBehavior(node.Type))
                {
                    document.Report(DiagnosticSeverity.Info, "preflight.inferred-control",
                        "「" + node.Name + "」的控件类型（" + node.Type.ToContract() +
                        "）是按图层名猜的，不是标签指定的；上线前建议用标签钉死", node);
                }
            }
        }

        private static bool IsInferred(UiNode node)
        {
            string source;
            return node.Tags != null && node.Tags.TryGetValue("type-source", out source) && source == "inferred";
        }

        /// <summary>会改变运行时行为、猜错了代价比较大的类型。</summary>
        private static bool HasBehavior(UiElementType type)
        {
            switch (type)
            {
                case UiElementType.Button:
                case UiElementType.TmpButton:
                case UiElementType.Toggle:
                case UiElementType.TmpToggle:
                case UiElementType.ToggleGroup:
                case UiElementType.Slider:
                case UiElementType.ScrollView:
                case UiElementType.Dropdown:
                case UiElementType.TmpDropdown:
                case UiElementType.InputField:
                case UiElementType.TmpInputField:
                case UiElementType.Mask:
                case UiElementType.List:
                case UiElementType.Grid:
                    return true;
                default:
                    return false;
            }
        }
    }
}
