using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Json;

namespace Psd2Ugui.Core.Pipeline
{
    /// <summary>
    /// 一次导入的体检报告。
    ///
    /// 它回答三个问题：这次导入花了多久、产出了什么、有没有需要人管的问题。
    /// 落盘成 JSON 后既能进 CI 产物，也能在编辑器窗口里展示（Step 10）。
    /// </summary>
    public sealed class ImportReport
    {
        public const string CurrentVersion = "1.0.0";

        public string Version = CurrentVersion;
        public string Generator = "psd2ugui";

        public string SourceFile = string.Empty;
        public string SourcePath = string.Empty;
        public string Module = string.Empty;
        public long SourceBytes;
        public int Width;
        public int Height;
        public int BitDepth = 8;
        public int LayerCount;

        /// <summary>解析 PSD 二进制的耗时（毫秒）。</summary>
        public double ParseMilliseconds;

        /// <summary>语义、导出计划、装配计划三段的耗时（毫秒）。</summary>
        public double PlanMilliseconds;

        public int Nodes;
        public int Groups;
        public int Sprites;
        public int SlicedSprites;
        public int SharedSprites;
        public int ReusedSprites;
        public long SpritePixels;
        public int ReferencesLocal;
        public int ReferencesShared;
        public int ReferencesExternal;

        public int Errors;
        public int Warnings;
        public int Infos;

        public readonly List<UiDiagnostic> Diagnostics = new List<UiDiagnostic>();
        public readonly Dictionary<string, string> Stats = new Dictionary<string, string>();

        public double TotalMilliseconds
        {
            get { return ParseMilliseconds + PlanMilliseconds; }
        }

        /// <summary>从「语义结果 + 导出计划」生成报告；传空计划表示还没走到导出这一步。</summary>
        public static ImportReport From(UiDocument document, ExportPlan plan = null)
        {
            var report = new ImportReport();
            if (document == null)
            {
                return report;
            }

            PsdMeta meta = document.Document;
            report.SourceFile = meta.FileName;
            report.SourcePath = meta.SourcePath;
            report.Module = meta.Module;
            report.Width = meta.Width;
            report.Height = meta.Height;
            report.BitDepth = meta.BitDepth;
            report.LayerCount = meta.LayerCount;
            report.Generator = document.Generator;

            report.Nodes = StatInt(document, "nodes");
            report.Groups = StatInt(document, "groups");
            if (plan != null)
            {
                report.Sprites = plan.Sprites.Count;
                report.SlicedSprites = plan.SliceableCount;
                report.SharedSprites = StatInt(document, "spriteShared");
                report.ReusedSprites = plan.Reused.Count;
                report.ReferencesLocal = plan.LocalReferences.Count;
                report.ReferencesShared = plan.SharedReferences.Count;
                report.ReferencesExternal = plan.ExternalReferences.Count;
                for (int i = 0; i < plan.Sprites.Count; i++)
                {
                    if (plan.Sprites[i].Bitmap != null)
                    {
                        report.SpritePixels += (long)plan.Sprites[i].Bitmap.Width * plan.Sprites[i].Bitmap.Height;
                    }
                }
            }

            report.Diagnostics.AddRange(document.Diagnostics);
            report.Errors = document.CountSeverity(DiagnosticSeverity.Error);
            report.Warnings = document.CountSeverity(DiagnosticSeverity.Warning);
            report.Infos = document.CountSeverity(DiagnosticSeverity.Info);

            var keys = new List<string>(document.Stats.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                report.Stats[keys[i]] = document.Stats[keys[i]];
            }

            return report;
        }

        private static int StatInt(UiDocument document, string key)
        {
            string value;
            int number;
            return document.Stats.TryGetValue(key, out value) && int.TryParse(value, out number) ? number : 0;
        }

        public string BuildSummary()
        {
            var builder = new StringBuilder();
            builder.Append(SourceFile).Append("：").Append(Nodes).Append(" 个节点，")
                .Append(Sprites).Append(" 张图（九宫 ").Append(SlicedSprites)
                .Append("，复用 ").Append(SharedSprites + ReusedSprites).Append("）");
            builder.Append("，诊断 ").Append(Diagnostics.Count)
                .Append(" (E").Append(Errors).Append("/W").Append(Warnings).Append("/I").Append(Infos).Append(')');
            builder.Append("，耗时 ").Append(Number(TotalMilliseconds)).Append(" ms");
            return builder.ToString();
        }

        public string ToJsonText(bool indented = true)
        {
            JsonValue root = JsonValue.Object()
                .Set("version", JsonValue.String(Version))
                .Set("generator", JsonValue.String(Generator))
                .Set("module", JsonValue.String(Module))
                .Set("source", JsonValue.Object()
                    .Set("file", JsonValue.String(SourceFile))
                    .Set("path", JsonValue.String(SourcePath))
                    .Set("bytes", JsonValue.Number(SourceBytes))
                    .Set("width", JsonValue.Number(Width))
                    .Set("height", JsonValue.Number(Height))
                    .Set("bitDepth", JsonValue.Number(BitDepth))
                    .Set("layers", JsonValue.Number(LayerCount)))
                .Set("timing", JsonValue.Object()
                    .Set("parseMs", JsonValue.Number(ParseMilliseconds))
                    .Set("planMs", JsonValue.Number(PlanMilliseconds))
                    .Set("totalMs", JsonValue.Number(TotalMilliseconds)))
                .Set("counts", JsonValue.Object()
                    .Set("nodes", JsonValue.Number(Nodes))
                    .Set("groups", JsonValue.Number(Groups))
                    .Set("sprites", JsonValue.Number(Sprites))
                    .Set("slicedSprites", JsonValue.Number(SlicedSprites))
                    .Set("sharedSprites", JsonValue.Number(SharedSprites))
                    .Set("reusedSprites", JsonValue.Number(ReusedSprites))
                    .Set("spritePixels", JsonValue.Number(SpritePixels))
                    .Set("referencesLocal", JsonValue.Number(ReferencesLocal))
                    .Set("referencesShared", JsonValue.Number(ReferencesShared))
                    .Set("referencesExternal", JsonValue.Number(ReferencesExternal)));

            JsonValue items = JsonValue.Array();
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                UiDiagnostic diagnostic = Diagnostics[i];
                items.Add(JsonValue.Object()
                    .Set("severity", JsonValue.String(diagnostic.Severity.ToContract()))
                    .Set("code", JsonValue.String(diagnostic.Code))
                    .Set("message", JsonValue.String(diagnostic.Message))
                    .Set("nodeId", JsonValue.String(diagnostic.NodeId))
                    .Set("layerPath", JsonValue.String(diagnostic.LayerPath))
                    .Set("layerId", JsonValue.Number(diagnostic.LayerId)));
            }

            root.Set("diagnostics", JsonValue.Object()
                .Set("error", JsonValue.Number(Errors))
                .Set("warning", JsonValue.Number(Warnings))
                .Set("info", JsonValue.Number(Infos))
                .Set("items", items));

            JsonValue stats = JsonValue.Object();
            var keys = new List<string>(Stats.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                stats.Set(keys[i], JsonValue.String(Stats[keys[i]]));
            }

            root.Set("stats", stats);
            return root.ToJsonString(indented);
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
