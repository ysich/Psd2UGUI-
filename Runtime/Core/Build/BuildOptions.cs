namespace Psd2Ugui.Core.Build
{
    /// <summary>预制体装配的可调项。</summary>
    public sealed class BuildOptions
    {
        /// <summary>模块名，留空表示用文档自己的模块名。</summary>
        public string Module;

        public int PixelsPerUnit = 100;

        /// <summary>标出每个节点来自哪个图层（挂 <c>Psd2UguiNode</c>）。</summary>
        public bool MarkNodes = true;

        /// <summary>隐藏图层仍然生成节点，只是 SetActive(false)。</summary>
        public bool IncludeHiddenNodes = true;

        /// <summary>复合控件补齐 uGUI 标准子结构（ScrollView 的 Viewport/Content 等）。</summary>
        public bool BuildTemplates = true;

        /// <summary>TMP 节点的文本效果用 TMP 自身的能力近似。</summary>
        public bool ApplyEffects = true;

        /// <summary>
        /// 工程是否安装了 TextMeshPro。未安装时 TMP 文本会回退到 uGUI Text，
        /// 同时使用 m_text_ 前缀，避免生成出指向不存在 TMP 组件的绑定。
        /// </summary>
        public bool UseTmpText = true;
    }
}
