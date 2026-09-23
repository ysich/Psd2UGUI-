namespace Psd2Ugui.Core.Pipeline
{
    /// <summary>资源导出的可调项。Core 侧只产出计划，落盘与导入设置由 Editor 侧执行。</summary>
    public sealed class ExportOptions
    {
        /// <summary>输出根目录（Unity 项目的 Assets 相对路径）。</summary>
        public string AssetRoot = "Assets/PSD2UGUI";

        /// <summary>
        /// 模块名，决定资源落在 AssetRoot 下的哪个子目录，同模块的资源互相复用。
        /// 留空表示用文档自己的模块名。
        /// </summary>
        public string Module;

        /// <summary>是否做九宫检测。</summary>
        public bool DetectNineSlice = true;

        /// <summary>隐藏图层是否也导出资源。</summary>
        public bool IncludeHiddenLayers = true;

        /// <summary>像素与 Unity 单位的换算，UI 通常用 100。</summary>
        public int PixelsPerUnit = 100;

        /// <summary>
        /// 贴图是否不压缩。UI 图默认不压缩，避免色带与边缘脏点；
        /// 图集庞大时可在项目侧改成压缩。
        /// </summary>
        public bool UncompressedTextures = true;

        /// <summary>超过这个边长就给「贴图过大」警示。</summary>
        public int MaxTextureSize = 4096;

        /// <summary>九宫检测的选项。</summary>
        public Imaging.NineSliceOptions NineSlice = new Imaging.NineSliceOptions();

        /// <summary>
        /// 已经导出过的共享贴图（其它界面/模块产出的）。
        /// 本文件里找不到的 `ref` 引用会来这里按「模块 / 名字」找，
        /// 找到就直接复用，同一张图不会在工程里存两份。留空表示不做跨文件复用。
        /// </summary>
        public SharedSpriteTable Shared;
    }
}
