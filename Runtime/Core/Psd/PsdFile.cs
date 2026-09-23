using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Psd
{
    public sealed class PsdChannelInfo
    {
        public int Id;
        public long Length;
        /// <summary>通道数据在文件中的绝对偏移（指向 2 字节压缩标记）。</summary>
        public int DataOffset = -1;
        public bool Decoded;
        public byte[] CachedData;
    }

    /// <summary>一条图层记录。字段按 PSD 规范原样保留，方便上层做语义判断。</summary>
    public sealed class PsdLayer
    {
        public int Index = -1;
        public int LayerId = -1;
        public int ParentLayerId = -1;
        public string Name = string.Empty;
        public string UnicodeName = string.Empty;
        public UiRect Rect = UiRect.Empty;
        public PsdChannelInfo[] Channels = new PsdChannelInfo[0];
        /// <summary>该层通道数据的每样本字节数（16/32 位 PSD 的 Lr16/Lr32 块会覆盖文档默认值）。</summary>
        public int BytesPerSample;
        public string BlendMode = "norm";
        public int Opacity = 255;
        public int FillOpacity = 255;
        public bool Clipping;
        public int Flags;
        public int DividerType = -1;
        public bool HasMask;
        public bool HasVectorMask;
        public bool HasSmartObject;
        public bool HasLegacyEffects;
        public bool BlendClipped;
        public bool BlendInteriorElements;
        public bool Knockout;
        public bool TransparencyShapesLayer;
        public bool Protected;
        public bool IsFillLayer;
        public bool HasSolidFill;
        public UiColor SolidFill = UiColor.White;
        public PsdTextEngineInfo Text;
        public List<UiEffect> Effects = new List<UiEffect>();
        public List<PsdLayer> Children = new List<PsdLayer>();
        public List<string> UnknownTags = new List<string>();

        public bool Visible
        {
            get { return (Flags & PsdLayerFlags.Hidden) == 0; }

        }

        /// <summary>是否为分组（folder）记录。</summary>
        public bool IsGroup
        {
            get { return DividerType == PsdSectionDivider.OpenFolder || DividerType == PsdSectionDivider.ClosedFolder; }
        }

        public bool IsBoundingDivider
        {
            get { return DividerType == PsdSectionDivider.BoundingSectionDivider; }
        }

        public string SectionKind
        {
            get { return PsdTextEngineInfo.DescribeDivider(DividerType); }
        }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(UnicodeName) ? Name : UnicodeName; }
        }

        public int Width
        {
            get { return (int)System.Math.Round(Rect.Width); }
        }

        public int Height
        {
            get { return (int)System.Math.Round(Rect.Height); }
        }

        /// <summary>是否为像素图层（有实际可合成的通道数据）。</summary>
        public bool HasPixelData
        {
            get
            {
                if (IsGroup || IsBoundingDivider)
                {
                    return false;
                }

                if (Width <= 0 || Height <= 0)
                {
                    return false;
                }

                for (int i = 0; i < Channels.Length; i++)
                {
                    if (Channels[i].Id >= PsdChannelId.Red)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public PsdChannelInfo FindChannel(int channelId)
        {
            for (int i = 0; i < Channels.Length; i++)
            {
                if (Channels[i].Id == channelId)
                {
                    return Channels[i];
                }
            }

            return null;
        }

        public string FullPath()
        {
            return string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(UnicodeName) ? "(未命名图层)" : DisplayName;
        }
    }

    public sealed class PsdImageResource
    {
        public string Signature = string.Empty;
        public int Id;
        public string Name = string.Empty;
        public int Length;
        public int Offset;
    }

    /// <summary>PSD 文件解析结果（结构 + 通道数据按需解码）。</summary>
    public sealed class PsdFile
    {
        public byte[] Data;
        public string FileName = string.Empty;
        public int Version = 1;
        public int ChannelCount;
        public int Width;
        public int Height;
        public int BitDepth = 8;
        public int ColorMode = 3;
        public double ResolutionPpi;
        public List<PsdImageResource> ImageResources = new List<PsdImageResource>();
        public List<PsdLayer> Layers = new List<PsdLayer>();
        public List<PsdLayer> RootLayers = new List<PsdLayer>();
        public int CompositeOffset = -1;
        public int CompositeCompression = -1;
        public List<string> Warnings = new List<string>();

        public bool IsPsb
        {
            get { return Version == 2; }
        }

        public PsdColorMode Mode
        {
            get { return (PsdColorMode)ColorMode; }
        }

        public int BytesPerSample
        {
            get { return BitDepth > 8 ? 2 : 1; }
        }

        public IEnumerable<PsdLayer> AllLayers()
        {
            foreach (PsdLayer layer in RootLayers)
            {
                foreach (PsdLayer item in Descend(layer))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<PsdLayer> Descend(PsdLayer layer)
        {
            yield return layer;
            for (int i = 0; i < layer.Children.Count; i++)
            {
                foreach (PsdLayer child in Descend(layer.Children[i]))
                {
                    yield return child;
                }
            }
        }

        public PsdLayer FindLayer(int layerId)
        {
            foreach (PsdLayer layer in AllLayers())
            {
                if (layer.LayerId == layerId)
                {
                    return layer;
                }
            }

            return null;
        }

        /// <summary>按需解码某个通道，结果会缓存。</summary>
        public byte[] ReadChannel(PsdLayer layer, PsdChannelInfo channel)
        {
            if (channel == null)
            {
                return null;
            }

            if (channel.Decoded)
            {
                return channel.CachedData;
            }

            int width = layer.Width;
            int height = layer.Height;
            if (channel.Id <= PsdChannelId.Transparency && (width <= 0 || height <= 0))
            {
                channel.Decoded = true;
                channel.CachedData = new byte[0];
                return channel.CachedData;
            }

            if (channel.DataOffset < 0 || channel.Length <= 2)
            {
                channel.Decoded = true;
                channel.CachedData = new byte[0];
                return channel.CachedData;
            }

            var reader = new PsdBinaryReader(Data, channel.DataOffset, (int)channel.Length);
            int compression = reader.ReadUInt16();
            int sampleBytes = layer.BytesPerSample > 0 ? layer.BytesPerSample : BytesPerSample;
            byte[] decoded = PsdChannelCodec.Decode(Data, reader.AbsolutePosition, reader.Remaining, compression,
                width, height, sampleBytes, IsPsb);
            channel.Decoded = true;
            channel.CachedData = decoded;
            return decoded;
        }
    }
}
