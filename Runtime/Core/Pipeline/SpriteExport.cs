using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Imaging;

namespace Psd2Ugui.Core.Pipeline
{
    /// <summary>一张要落盘的 Sprite：位图 + 九宫 + 身份信息。</summary>
    public sealed class SpriteExport
    {
        /// <summary>资源身份，重新导出时保持不变（节点身份 → 资源身份）。</summary>
        public string ResourceId = string.Empty;

        /// <summary>资源名（不含扩展名，用于展示与 `ref` 查找）。</summary>
        public string Name = string.Empty;

        /// <summary>相对模块目录的文件名。</summary>
        public string FileName = string.Empty;

        public Bitmap Bitmap;

        /// <summary>九宫边框（全 0 表示不切）。</summary>
        public UiBorder Border;

        /// <summary>内容哈希：一样的内容会共用一张图。</summary>
        public string ContentHash = string.Empty;

        /// <summary>使用这张图的节点（第一个是产出它的节点）。</summary>
        public readonly List<string> NodeIds = new List<string>();

        /// <summary>使用这张图的节点名（用于 `ref` 按名字查找）。</summary>
        public readonly List<string> NodeNames = new List<string>();

        /// <summary>来源图层信息，便于排查。</summary>
        public int SourceLayerId = -1;

        public string SourceLayerPath = string.Empty;

        /// <summary>
        /// 贴图覆盖图层位图的哪一块（左上角 + 尺寸）。
        /// 裁剪导出的图比图层矩形小，装配侧会把节点矩形一起收进这块；补回透明边的图就是整张。
        /// </summary>
        public UiRect SourceRect = UiRect.Empty;

        /// <summary>是否被多个节点共用（含 `ref` 引用）。</summary>
        public bool Shared;

        /// <summary>是否是 `ref` / 组内引用命中的资源（不重复落盘）。</summary>
        public bool IsReferenced;

        public bool IsSliceable
        {
            get { return Border != null && !Border.IsZero; }
        }

        public string Key
        {
            get { return ContentHash + "|" + Border.Left + "," + Border.Bottom + "," + Border.Right + "," + Border.Top; }
        }
    }
}
