namespace Psd2Ugui.Core.Contract
{
    /// <summary>需要落盘的资源（图片 / 字体）。</summary>
    public sealed class UiResource
    {
        public string Id = string.Empty;
        public UiResourceKind Kind = UiResourceKind.Sprite;
        public string Name = string.Empty;
        public string Module = "common";
        public string FileName = string.Empty;
        public string ContentHash = string.Empty;
        public int Width;
        public int Height;
        public UiBorder Border;
        public int SourceLayerId = -1;
        public string SourceLayerPath = string.Empty;
        public UiRect SourceRect = UiRect.Empty;
        public bool Shared;

        public string ContractPath
        {
            get
            {
                string folder = Kind == UiResourceKind.Texture ? "texture" : "sprite";
                return folder + "/" + Module + "/" + FileName;
            }
        }
    }

    /// <summary>解析或生成过程中的一条诊断。</summary>
    public sealed class UiDiagnostic
    {
        public UiDiagnostic(DiagnosticSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public DiagnosticSeverity Severity { get; private set; }

        public string Code { get; private set; }

        public string Message { get; private set; }

        public string LayerPath { get; set; }

        public string NodeId { get; set; }

        public int LayerId { get; set; }

        public override string ToString()
        {
            return "[" + Severity.ToContract() + "] " + Code + ": " + Message +
                   (string.IsNullOrEmpty(LayerPath) ? string.Empty : " (图层: " + LayerPath + ")");
        }
    }
}
