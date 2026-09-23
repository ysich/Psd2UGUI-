using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Psd2Ugui.Testing
{
    /// <summary>图层记录的一种通道写法。Payload 不含 2 字节压缩头。</summary>
    internal sealed class ChannelSpec
    {
        public int Id;
        public int Compression;
        public byte[] Payload = new byte[0];

        /// <summary>为 true 时声明长度但不在文件里写数据，用来构造截断文件。</summary>
        public bool OmitData;
    }

    internal sealed class LayerSpec
    {
        public string Name = "Layer";
        public int Top;
        public int Left;
        public int Right = 2;
        public int Bottom = 2;
        public int Opacity = 255;
        public int Flags = 8;
        public bool Clipping;
        public string BlendMode = "norm";

        /// <summary>图层蒙版数据长度，大于 0 时写入等量的 0 字节。</summary>
        public int MaskDataLength;

        public int BlendingRangesLength = 8;

        /// <summary>故意让附加数据的长度字段与真实字节数不一致。</summary>
        public int ExtraLengthDelta;

        public readonly List<ChannelSpec> Channels = new List<ChannelSpec>();
        public readonly List<KeyValuePair<string, byte[]>> Tags = new List<KeyValuePair<string, byte[]>>();

        public LayerSpec WithTag(string key, byte[] payload)
        {
            Tags.Add(new KeyValuePair<string, byte[]>(key, payload));
            return this;
        }

        public LayerSpec WithChannel(int id, int compression, byte[] payload)
        {
            Channels.Add(new ChannelSpec { Id = id, Compression = compression, Payload = payload });
            return this;
        }
    }

    /// <summary>
    /// 构造最小可解析的 PSD 字节流，用于在没有真实素材的机器上回归解析器。
    /// 布局严格按规范：文件头 / 颜色模式 / 图像资源 / 图层与蒙版 / 合成图像。
    /// </summary>
    internal sealed class PsdFixtureBuilder
    {
        public int Width = 4;
        public int Height = 4;
        public int ChannelCount = 4;
        public int Depth = 8;
        public int ColorMode = 3;
        public int Version = 1;

        /// <summary>文档级附加信息块按 4 字节对齐（Photoshop 的真实行为）。</summary>
        public bool PadDocumentBlocks = true;

        /// <summary>大于等于 0 时用这个值覆盖图层数量字段，用来构造异常文件。</summary>
        public int DeclaredLayerCount = -1;

        public bool OmitCompositeHeader;

        /// <summary>
        /// 新图层自动带上 lyid（Photoshop 的真实行为）。
        /// 置 false 可以构造“没有图层编号”的老文件，用来验证解析器的兜底逻辑。
        /// </summary>
        public bool AutoLayerIds = true;

        private int _nextLayerId = 1;

        public readonly List<KeyValuePair<int, byte[]>> Resources = new List<KeyValuePair<int, byte[]>>();
        public readonly List<LayerSpec> Layers = new List<LayerSpec>();
        public readonly List<KeyValuePair<string, byte[]>> DocumentBlocks = new List<KeyValuePair<string, byte[]>>();

        public LayerSpec AddLayer(LayerSpec layer)
        {
            if (AutoLayerIds && layer != null)
            {
                layer.WithTag("lyid", Desc.I32(_nextLayerId));
            }

            _nextLayerId++;
            Layers.Add(layer);
            return layer;
        }

        public LayerSpec AddLayer(string name)
        {
            return AddLayer(new LayerSpec { Name = name });
        }

        public byte[] Build()
        {
            var file = new List<byte>();
            file.AddRange(Encoding.ASCII.GetBytes("8BPS"));
            WriteU16(file, (ushort)Version);
            file.AddRange(new byte[6]);
            WriteU16(file, (ushort)ChannelCount);
            WriteI32(file, Height);
            WriteI32(file, Width);
            WriteU16(file, (ushort)Depth);
            WriteU16(file, (ushort)ColorMode);

            WriteI32(file, 0); // 颜色模式数据

            if (Resources.Count == 0)
            {
                WriteI32(file, 0);
            }
            else
            {
                var block = new List<byte>();
                for (int i = 0; i < Resources.Count; i++)
                {
                    block.AddRange(Encoding.ASCII.GetBytes("8BIM"));
                    WriteU16(block, (ushort)Resources[i].Key);
                    block.Add(0); // 空名字
                    block.Add(0); // 补齐到 2 字节
                    WriteI32(block, Resources[i].Value.Length);
                    block.AddRange(Resources[i].Value);
                    if (Resources[i].Value.Length % 2 != 0)
                    {
                        block.Add(0);
                    }
                }

                WriteI32(file, block.Count);
                file.AddRange(block);
            }

            byte[] section = BuildLayerAndMaskSection();
            if (Version == 2)
            {
                WriteI64(file, section.Length);
            }
            else
            {
                WriteI32(file, section.Length);
            }

            file.AddRange(section);

            if (!OmitCompositeHeader)
            {
                WriteU16(file, 0); // 合成图像压缩方式
            }

            return file.ToArray();
        }

        private byte[] BuildLayerAndMaskSection()
        {
            var section = new List<byte>();
            byte[] layerInfo = BuildLayerInfo();

            if (Version == 2)
            {
                WriteI64(section, layerInfo.Length);
            }
            else
            {
                WriteI32(section, layerInfo.Length);
            }

            section.AddRange(layerInfo);
            WriteI32(section, 0); // 全局图层蒙版信息长度 0

            int pivot = section.Count;
            for (int i = 0; i < DocumentBlocks.Count; i++)
            {
                section.AddRange(Encoding.ASCII.GetBytes("8BIM"));
                section.AddRange(Encoding.ASCII.GetBytes(DocumentBlocks[i].Key.PadRight(4).Substring(0, 4)));
                WriteI32(section, DocumentBlocks[i].Value.Length);
                section.AddRange(DocumentBlocks[i].Value);

                if (PadDocumentBlocks)
                {
                    int misalignment = (section.Count - pivot) % 4;
                    if (misalignment != 0)
                    {
                        section.AddRange(new byte[4 - misalignment]);
                    }
                }
            }

            return section.ToArray();
        }

        private byte[] BuildLayerInfo()
        {
            var body = new List<byte>();
            if (Version == 2)
            {
                WriteI64(body, DeclaredLayerCount >= 0 ? DeclaredLayerCount : Layers.Count);
            }
            else
            {
                WriteI16(body, (short)(DeclaredLayerCount >= 0 ? DeclaredLayerCount : Layers.Count));
            }

            for (int i = 0; i < Layers.Count; i++)
            {
                body.AddRange(BuildLayerRecord(Layers[i]));
            }

            for (int i = 0; i < Layers.Count; i++)
            {
                LayerSpec layer = Layers[i];
                for (int c = 0; c < layer.Channels.Count; c++)
                {
                    ChannelSpec channel = layer.Channels[c];
                    if (channel.OmitData)
                    {
                        continue;
                    }

                    WriteU16(body, (ushort)channel.Compression);
                    body.AddRange(channel.Payload);
                }
            }

            while (body.Count % 4 != 0)
            {
                body.Add(0);
            }

            return body.ToArray();
        }

        private byte[] BuildLayerRecord(LayerSpec layer)
        {
            var record = new List<byte>();
            WriteI32(record, layer.Top);
            WriteI32(record, layer.Left);
            WriteI32(record, layer.Bottom);
            WriteI32(record, layer.Right);

            WriteU16(record, (ushort)layer.Channels.Count);
            for (int i = 0; i < layer.Channels.Count; i++)
            {
                ChannelSpec channel = layer.Channels[i];
                WriteI16(record, (short)channel.Id);
                // OmitData 只是不写字节，长度字段照旧，用来构造“声明了但数据缺失”的文件
                long length = 2 + channel.Payload.Length;

                if (Version == 2)
                {
                    WriteI64(record, length);
                }
                else
                {
                    WriteI32(record, (int)length);

                }
            }

            record.AddRange(Encoding.ASCII.GetBytes("8BIM"));
            record.AddRange(Encoding.ASCII.GetBytes(layer.BlendMode.PadRight(4).Substring(0, 4)));
            record.Add((byte)layer.Opacity);
            record.Add((byte)(layer.Clipping ? 1 : 0));
            record.Add((byte)layer.Flags);
            record.Add(0);

            var extra = new List<byte>();
            WriteI32(extra, layer.MaskDataLength);
            if (layer.MaskDataLength > 0)
            {
                extra.AddRange(new byte[layer.MaskDataLength]);
            }

            WriteI32(extra, layer.BlendingRangesLength);
            if (layer.BlendingRangesLength > 0)
            {
                extra.AddRange(new byte[layer.BlendingRangesLength]);
            }

            extra.AddRange(WritePascal4(layer.Name));

            for (int i = 0; i < layer.Tags.Count; i++)
            {
                extra.AddRange(Encoding.ASCII.GetBytes("8BIM"));
                extra.AddRange(Encoding.ASCII.GetBytes(layer.Tags[i].Key.PadRight(4).Substring(0, 4)));
                WriteI32(extra, layer.Tags[i].Value.Length);
                extra.AddRange(layer.Tags[i].Value);
            }

            if (extra.Count % 2 != 0)
            {
                extra.Add(0);
            }

            WriteI32(record, extra.Count + layer.ExtraLengthDelta);
            record.AddRange(extra);
            return record.ToArray();
        }

        private static byte[] WritePascal4(string name)
        {
            byte[] ascii = Encoding.ASCII.GetBytes(name.Length > 255 ? name.Substring(0, 255) : name);
            var result = new List<byte>();
            result.Add((byte)ascii.Length);
            result.AddRange(ascii);
            while (result.Count % 4 != 0)
            {
                result.Add(0);
            }

            return result.ToArray();
        }

        public static void WriteU16(List<byte> target, ushort value)
        {
            target.Add((byte)(value >> 8));
            target.Add((byte)value);
        }

        public static void WriteI16(List<byte> target, short value)
        {
            WriteU16(target, unchecked((ushort)value));
        }

        public static void WriteI32(List<byte> target, int value)
        {
            target.Add((byte)(value >> 24));
            target.Add((byte)(value >> 16));
            target.Add((byte)(value >> 8));
            target.Add((byte)value);
        }

        public static void WriteI64(List<byte> target, long value)
        {
            for (int shift = 56; shift >= 0; shift -= 8)
            {
                target.Add((byte)(value >> shift));
            }
        }

        public static void WriteF64(List<byte> target, double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            target.AddRange(bytes);
        }

        public static byte[] Concat(params byte[][] parts)
        {
            var result = new List<byte>();
            for (int i = 0; i < parts.Length; i++)
            {
                result.AddRange(parts[i]);
            }

            return result.ToArray();
        }
    }

    /// <summary>按 PSD 描述符的线格式拼字节。</summary>
    internal static class Desc
    {
        public static byte[] Ascii(string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        public static byte[] U16(ushort value)
        {
            return new byte[] { (byte)(value >> 8), (byte)value };
        }

        public static byte[] I32(int value)
        {
            var bytes = new List<byte>();
            PsdFixtureBuilder.WriteI32(bytes, value);
            return bytes.ToArray();
        }

        /// <summary>长度 0 表示后随 4 字节已知术语；超过 4 字符的键要写成带长度的形式。</summary>
        public static byte[] Key(string key)
        {
            if (key.Length != 4)
            {
                return LengthKey(key);
            }

            return PsdFixtureBuilder.Concat(I32(0), Ascii(key));
        }


        public static byte[] LengthKey(string key)
        {
            return PsdFixtureBuilder.Concat(I32(key.Length), Ascii(key));
        }

        private static byte[] Item(string typeId, byte[] payload)
        {
            return PsdFixtureBuilder.Concat(Ascii(typeId), payload);
        }

        public static byte[] Double(double value)
        {
            var bytes = new List<byte>();
            PsdFixtureBuilder.WriteF64(bytes, value);
            return Item("doub", bytes.ToArray());
        }

        public static byte[] Bool(bool value)
        {
            return Item("bool", new[] { (byte)(value ? 1 : 0) });
        }

        public static byte[] Long(int value)
        {
            return Item("long", I32(value));
        }

        public static byte[] Unit(string unit, double value)
        {
            var bytes = new List<byte>();
            bytes.AddRange(Ascii(unit));
            PsdFixtureBuilder.WriteF64(bytes, value);
            return Item("UntF", bytes.ToArray());
        }

        public static byte[] Enum(string type, string value)
        {
            return Item("enum", PsdFixtureBuilder.Concat(Key(type), Key(value)));
        }

        public static byte[] Text(string value)
        {
            var bytes = new List<byte>();
            PsdFixtureBuilder.WriteI32(bytes, value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                PsdFixtureBuilder.WriteU16(bytes, value[i]);
            }

            return Item("TEXT", bytes.ToArray());
        }

        public static byte[] Raw(byte[] data)
        {
            return Item("tdta", PsdFixtureBuilder.Concat(I32(data.Length), data));
        }

        /// <summary>描述符里的一项：键 + 带类型前缀的值。</summary>
        public static byte[] Entry(string key, byte[] item)
        {
            return PsdFixtureBuilder.Concat(Key(key), item);
        }

        public static byte[] List(params byte[][] items)

        {
            var bytes = new List<byte>();
            PsdFixtureBuilder.WriteI32(bytes, items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                bytes.AddRange(items[i]);
            }

            return Item("VlLs", bytes.ToArray());
        }

        public static byte[] Object(string classId, params byte[][] items)
        {
            return Item("Objc", ObjectBody(classId, items));
        }

        /// <summary>带 name 字段的描述符主体（Photoshop 写的 name 长度通常是 0）。</summary>
        public static byte[] ObjectBody(string classId, params byte[][] items)
        {
            var bytes = new List<byte>();
            PsdFixtureBuilder.WriteI32(bytes, 0); // 名字长度 0
            bytes.AddRange(Key(classId));
            PsdFixtureBuilder.WriteI32(bytes, items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                bytes.AddRange(items[i]);
            }

            return bytes.ToArray();
        }

        /// <summary>不带 name 字段的旧版描述符主体，用来触发解析器的回退分支。</summary>
        public static byte[] LegacyObjectBody(string classId, params byte[][] items)
        {
            var bytes = new List<byte>();
            bytes.AddRange(Key(classId));
            PsdFixtureBuilder.WriteI32(bytes, items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                bytes.AddRange(items[i]);
            }

            return bytes.ToArray();
        }
    }
}
