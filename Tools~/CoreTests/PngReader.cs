using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Psd2Ugui.Core.Imaging;

namespace Psd2Ugui.CoreTests
{
    /// <summary>
    /// 测试用的最小 PNG 解码器：只认 RGBA32 非隔行，
    /// 但会严格校验签名、每个 chunk 的 CRC、zlib 头与 Adler32，
    /// 从而验证编码器写出来的不是一个“自己认得、别人不认得”的文件。
    /// </summary>
    internal sealed class PngReader
    {
        public int Width;
        public int Height;
        public int BitDepth;
        public int ColorType;
        public byte[] Pixels = new byte[0];

        /// <summary>每行第一个字节（滤波方式），用于检查编码器真的挑了滤波。</summary>
        public List<byte> RowFilters = new List<byte>();

        public bool SawIend;

        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static PngReader Decode(byte[] data)
        {
            var reader = new PngReader();
            for (int i = 0; i < Signature.Length; i++)
            {
                if (data[i] != Signature[i])
                {
                    throw new InvalidDataException("PNG 签名错误");
                }
            }

            int position = Signature.Length;
            var idat = new MemoryStream();
            byte[] raw = null;
            while (position + 8 <= data.Length)
            {
                int length = ReadInt32(data, position);
                string type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);

                var payload = new byte[length + 4];
                Array.Copy(data, position + 4, payload, 0, length + 4);
                uint expectedCrc = PngEncoder.Crc32(payload);
                uint actualCrc = (uint)ReadInt32(data, position + 8 + length);
                if (expectedCrc != actualCrc)
                {
                    throw new InvalidDataException(type + " chunk 的 CRC 不对");
                }

                if (type == "IHDR")
                {
                    reader.Width = ReadInt32(data, position + 8);
                    reader.Height = ReadInt32(data, position + 12);
                    reader.BitDepth = data[position + 16];
                    reader.ColorType = data[position + 17];
                    if (data[position + 18] != 0 || data[position + 19] != 0 || data[position + 20] != 0)
                    {
                        throw new InvalidDataException("压缩/滤波/隔行字段必须为 0");
                    }
                }
                else if (type == "IDAT")
                {
                    idat.Write(data, position + 8, length);
                }
                else if (type == "IEND")
                {
                    reader.SawIend = true;
                }

                position += 12 + length;
            }

            byte[] compressed = idat.ToArray();
            if (compressed.Length < 6 || compressed[0] != 0x78)
            {
                throw new InvalidDataException("缺少 zlib 头");
            }

            int adlerOffset = compressed.Length - 4;
            using (var stream = new MemoryStream(compressed, 2, adlerOffset - 2))
            using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                deflate.CopyTo(output);
                raw = output.ToArray();
            }

            uint expectedAdler = PngEncoder.Adler32(raw);
            uint actualAdler = ((uint)compressed[adlerOffset] << 24) | ((uint)compressed[adlerOffset + 1] << 16) |
                               ((uint)compressed[adlerOffset + 2] << 8) | compressed[adlerOffset + 3];
            if (expectedAdler != actualAdler)
            {
                throw new InvalidDataException("Adler32 不对");
            }

            reader.Pixels = Unfilter(raw, reader.Width, reader.Height, reader.RowFilters);
            return reader;
        }

        private static byte[] Unfilter(byte[] raw, int width, int height, List<byte> filters)
        {
            int stride = width * 4;
            var pixels = new byte[stride * height];
            var previous = new byte[stride];
            var current = new byte[stride];
            int position = 0;
            for (int y = 0; y < height; y++)
            {
                byte filter = raw[position++];
                filters.Add(filter);
                Array.Copy(raw, position, current, 0, stride);
                position += stride;

                for (int i = 0; i < stride; i++)
                {
                    int left = i >= 4 ? current[i - 4] : 0;
                    int up = previous[i];
                    int upLeft = i >= 4 ? previous[i - 4] : 0;
                    switch (filter)
                    {
                        case 0:
                            break;
                        case 1:
                            current[i] = (byte)(current[i] + left);
                            break;
                        case 2:
                            current[i] = (byte)(current[i] + up);
                            break;
                        case 3:
                            current[i] = (byte)(current[i] + ((left + up) >> 1));
                            break;
                        case 4:
                            current[i] = (byte)(current[i] + Paeth(left, up, upLeft));
                            break;
                        default:
                            throw new InvalidDataException("未知滤波方式 " + filter);
                    }
                }

                Array.Copy(current, 0, pixels, y * stride, stride);
                Array.Copy(current, 0, previous, 0, stride);
            }

            return pixels;
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            int estimate = left + up - upLeft;
            int distanceLeft = Math.Abs(estimate - left);
            int distanceUp = Math.Abs(estimate - up);
            int distanceUpLeft = Math.Abs(estimate - upLeft);
            if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
            {
                return left;
            }

            return distanceUp <= distanceUpLeft ? up : upLeft;
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
