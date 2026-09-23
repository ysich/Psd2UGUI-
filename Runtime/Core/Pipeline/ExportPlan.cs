using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Pipeline
{
    /// <summary>一次导出要做的事：要写的图、要复用的图、以及契约资源表。</summary>
    public sealed class ExportPlan
    {
        public string AssetRoot = string.Empty;
        public string Module = string.Empty;

        public readonly List<SpriteExport> Sprites = new List<SpriteExport>();

        public readonly List<UiResource> Resources = new List<UiResource>();

        /// <summary>按名字复用了本文件内已有资源的引用节点。</summary>
        public readonly List<UiNode> LocalReferences = new List<UiNode>();

        /// <summary>目标不在本文件内、需要去共享资源库里找的引用节点。</summary>
        public readonly List<UiNode> ExternalReferences = new List<UiNode>();

        public int SliceableCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Sprites.Count; i++)
                {
                    if (Sprites[i].IsSliceable)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public long TotalBytes
        {
            get
            {
                long bytes = 0;
                for (int i = 0; i < Sprites.Count; i++)
                {
                    bytes += Sprites[i].Bitmap == null ? 0 : Sprites[i].Bitmap.Pixels.Length;
                }

                return bytes;
            }
        }

        public SpriteExport FindByResourceId(string id)
        {
            for (int i = 0; i < Sprites.Count; i++)
            {
                if (Sprites[i].ResourceId == id)
                {
                    return Sprites[i];
                }
            }

            return null;
        }
    }
}
