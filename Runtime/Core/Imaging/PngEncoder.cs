using System;
using System.IO;
using System.IO.Compression;

namespace Psd2Ugui.Core.Imaging
{
    /// <summary>
    /// 自研 PNG 编码器（RGBA32 / 8 位）。
    /// 只依赖 BCL 的 DeflateStream，不引第三方库；写入 zlib 头与 Adler32，
    /// 让 Unity、浏览器、系统预览都能直接打开。
    ///
    /// 每行按「None/Sub/Up/Average/Paeth」五种滤波里选一个最小的写，
    /// 贴图多为大片纯色，这样压出来的文件比不滤波小很多。
    /// </summary>
    public static class PngEncoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] Encode(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException("bitmap");
            }

            return EncodeRgba(bitmap.Width, bitmap.Height, bitmap.Pixels);
        }

        public static byte[] EncodeRgba(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("PNG 尺寸非法: " + width + "x" + height);
            }

            int stride = width * 4;
            if (rgba == null || rgba.Length < stride * height)
            {
                throw new ArgumentException("像素数据长度不足");
            }

            byte[] raw = Filter(rgba, width, height, stride);
            byte[] compressed = Zlib(raw);

            using (var stream = new MemoryStream())
            {
                stream.Write(Signature, 0, Signature.Length);
                WriteChunk(stream, "IHDR", Header(width, height));
                WriteChunk(stream, "IDAT", compressed);
                WriteChunk(stream, "IEND", new byte[0]);
                return stream.ToArray();
            }
        }

        public static void Write(string path, Bitmap bitmap)
        {
            byte[] data = Encode(bitmap);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, data);
        }

        private static byte[] Header(int width, int height)
        {
            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8; // 位深
            header[9] = 6; // 颜色类型：RGBA
            header[10] = 0; // 压缩方法
            header[11] = 0; // 滤波方法
            header[12] = 0; // 隔行扫描：无
            return header;
        }

        /// <summary>按行挑选滤波方式，返回「滤波字节 + 滤波后数据」的连续缓冲区。</summary>
        private static byte[] Filter(byte[] rgba, int width, int height, int stride)
        {
            var output = new byte[height * (stride + 1)];
            var previous = new byte[stride];
            var current = new byte[stride];
            var candidate = new byte[stride];
            var best = new byte[stride];

            int position = 0;
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(rgba, y * stride, current, 0, stride);

                byte filter = 0;
                long bestScore = long.MaxValue;
                for (byte type = 0; type <= 4; type++)
                {
                    long score = ApplyFilter(type, current, previous, candidate, stride);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        filter = type;
                        Buffer.BlockCopy(candidate, 0, best, 0, stride);
                    }
                }

                output[position++] = filter;
                Buffer.BlockCopy(best, 0, output, position, stride);
                position += stride;

                Buffer.BlockCopy(current, 0, previous, 0, stride);
            }

            return output;
        }

        /// <summary>写出某一种滤波结果，返回“绝对值和”，越小说明这行越适合这种滤波。</summary>
        private static long ApplyFilter(byte type, byte[] current, byte[] previous, byte[] output, int stride)
        {
            long score = 0;
            for (int i = 0; i < stride; i++)
            {
                int left = i >= 4 ? current[i - 4] : 0;
                int up = previous[i];
                int upLeft = i >= 4 ? previous[i - 4] : 0;
                int value;
                switch (type)
                {
                    case 1:
                        value = current[i] - left;
                        break;
                    case 2:
                        value = current[i] - up;
                        break;
                    case 3:
                        value = current[i] - ((left + up) >> 1);
                        break;
                    case 4:
                        value = current[i] - Paeth(left, up, upLeft);
                        break;
                    default:
                        value = current[i];
                        break;
                }

                byte b = (byte)(value & 0xFF);
                output[i] = b;
                // 有符号字节的绝对值和是标准做法（0 与 -1 都算“便宜”）
                score += b < 128 ? b : 256 - b;
            }

            return score;
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

        /// <summary>原始 deflate 前面套一层 zlib 头，尾部补 Adler32。</summary>
        private static byte[] Zlib(byte[] data)
        {
            using (var stream = new MemoryStream())
            {
                stream.WriteByte(0x78);
                stream.WriteByte(0x9C);
                using (var deflate = new DeflateStream(stream, CompressionLevel.Optimal, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                uint adler = Adler32(data);
                stream.WriteByte((byte)(adler >> 24));
                stream.WriteByte((byte)(adler >> 16));
                stream.WriteByte((byte)(adler >> 8));
                stream.WriteByte((byte)adler);
                return stream.ToArray();
            }
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            stream.Write(length, 0, 4);

            var payload = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++)
            {
                payload[i] = (byte)type[i];
            }

            Buffer.BlockCopy(data, 0, payload, 4, data.Length);
            stream.Write(payload, 0, payload.Length);

            var crc = new byte[4];
            WriteBigEndian(crc, 0, Crc32(payload));
            stream.Write(crc, 0, 4);
        }

        private static void WriteBigEndian(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }

        public static uint Adler32(byte[] data)

        {
            const uint Modulus = 65521;
            uint a = 1;
            uint b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a = (a + data[i]) % Modulus;
                b = (b + a) % Modulus;
            }

            return (b << 16) | a;
        }

        public static uint Crc32(byte[] data, int offset = 0, int length = -1)
        {
            if (length < 0)
            {
                length = data.Length - offset;
            }

            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < length; i++)
            {
                crc = CrcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
