using System.Collections.Generic;
using System.Text;

namespace Psd2Ugui.Core.Contract
{
    /// <summary>PSD 文档元信息。</summary>
    public sealed class PsdMeta
    {
        public string Name = string.Empty;
        public string FileName = string.Empty;
        public string SourcePath = string.Empty;
        public string Module = "common";
        public int Width;
        public int Height;
        public int ChannelCount;
        public int BitDepth = 8;
        public int ColorMode;
        public int LayerCount;
        public double ResolutionPpi;
        public string GeneratorVersion = string.Empty;
    }

    /// <summary>
    /// 引擎无关的中间契约。Unity 侧只消费这份数据，便于排查与将来对接其他引擎。
    /// </summary>
    public sealed class UiDocument
    {
        public const string CurrentSchemaVersion = "1.0.0";

        public string SchemaVersion = CurrentSchemaVersion;
        public string Generator = "psd2ugui";
        public PsdMeta Document = new PsdMeta();
        public List<UiResource> Resources = new List<UiResource>();
        public List<UiDiagnostic> Diagnostics = new List<UiDiagnostic>();
        public UiNode Root;
        public Dictionary<string, string> Stats = new Dictionary<string, string>();

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Diagnostics.Count; i++)
                {
                    if (Diagnostics[i].Severity == DiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int CountSeverity(DiagnosticSeverity severity)
        {
            int count = 0;
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity == severity)
                {
                    count++;
                }
            }

            return count;
        }

        public UiDiagnostic Report(DiagnosticSeverity severity, string code, string message, UiNode node = null)
        {
            var diagnostic = new UiDiagnostic(severity, code, message);
            if (node != null)
            {
                diagnostic.NodeId = node.Id;
                diagnostic.LayerPath = node.LayerPath;
                diagnostic.LayerId = node.LayerId;
            }

            Diagnostics.Add(diagnostic);
            return diagnostic;
        }

        public UiResource FindResource(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < Resources.Count; i++)
            {
                if (Resources[i].Id == id)
                {
                    return Resources[i];
                }
            }

            return null;
        }

        public UiNode FindNode(string id)
        {
            return Root == null ? null : Root.FindById(id);
        }

        public IEnumerable<UiNode> Nodes()
        {
            if (Root == null)
            {
                yield break;
            }

            foreach (UiNode node in Root.SelfAndDescendants())
            {
                yield return node;
            }
        }

        public string BuildSummary()
        {
            var builder = new StringBuilder();
            builder.Append(Document.Name).Append(" [")
                .Append(Document.Width).Append('x').Append(Document.Height).Append(", ")
                .Append(Document.BitDepth).Append("bit]");
            builder.Append('\n');
            builder.Append("节点 ").Append(Root == null ? 0 : Root.CountDescendants())
                .Append(", 资源 ").Append(Resources.Count)
                .Append(", 诊断 ").Append(Diagnostics.Count)
                .Append(" (E").Append(CountSeverity(DiagnosticSeverity.Error))
                .Append("/W").Append(CountSeverity(DiagnosticSeverity.Warning))
                .Append("/I").Append(CountSeverity(DiagnosticSeverity.Info)).Append(')');
            foreach (KeyValuePair<string, string> pair in Stats)
            {
                builder.Append('\n').Append(pair.Key).Append(": ").Append(pair.Value);
            }

            return builder.ToString();
        }
    }
}
