using System;
using System.IO;
using System.IO.Compression;

namespace Psd2Ugui.Core.Psd
{
    /// <summary>
    /// 通道数据解码：Raw / RLE(PackBits) / ZIP / ZIP+Prediction。
    /// 统一输出原始采样字节（每样本 bytesPerSample 字节，大端序）。
    /// </summary>
    public static class PsdChannelCodec
    {
        public static byte[] Decode(byte[] data, int offset, int length, int compression, int width, int height,
            int bytesPerSample, bool psb)
        {
            int expected = width * height * bytesPerSample;
            if (expected <= 0)
            {
                return new byte[0];
            }

            int rowBytes = width * bytesPerSample;
            switch (compression)
            {
                case PsdCompression.Raw:
                    return DecodeRaw(data, offset, length, expected);
                case PsdCompression.Rle:
                    return DecodeRle(data, offset, length, rowBytes, height, psb);
                case PsdCompression.Zip:
                    return Inflate(data, offset, length, expected);
                case PsdCompression.ZipWithPrediction:
                    byte[] predicted = Inflate(data, offset, length, expected);
                    UndoPrediction(predicted, rowBytes, height, bytesPerSample);
                    return predicted;

                default:
                    throw new PsdParseException("不支持的通道压缩方式: " + compression, offset);
            }
        }

        private static byte[] DecodeRaw(byte[] data, int offset, int length, int expected)
        {
            byte[] result = new byte[expected];
            int available = Math.Min(length, expected);
            Array.Copy(data, offset, result, 0, available);
            return result;
        }

        private static byte[] DecodeRle(byte[] data, int offset, int length, int rowBytes, int height, bool psb)
        {
            int countSize = psb ? 4 : 2;
            var rowLengths = new int[height];
            int cursor = offset;
            int limit = offset + length;

            for (int y = 0; y < height; y++)
            {
                if (cursor + countSize > limit)
                {
                    throw new PsdParseException("RLE 行长度表不完整", offset);
                }

                if (psb)
                {
                    rowLengths[y] = (data[cursor] << 24) | (data[cursor + 1] << 16) |
                                    (data[cursor + 2] << 8) | data[cursor + 3];
                }
                else
                {
                    rowLengths[y] = (data[cursor] << 8) | data[cursor + 1];
                }

                cursor += countSize;
            }

            byte[] result = new byte[rowBytes * height];
            int written = 0;
            for (int y = 0; y < height; y++)
            {
                int packed = rowLengths[y];
                if (cursor + packed > limit)
                {
                    packed = Math.Max(0, limit - cursor);
                }

                int produced = UnpackBits(data, cursor, packed, result, written, rowBytes);
                cursor += packed;
                written += rowBytes;
                if (produced < rowBytes)
                {
                    // 行内数据不足时保持透明，不中断整个解析
                    for (int i = written - (rowBytes - produced); i < written; i++)
                    {
                        result[i] = 0;
                    }
                }
            }

            return result;
        }

        /// <summary>PackBits 解压，返回实际写入的目标字节数。</summary>
        public static int UnpackBits(byte[] source, int offset, int length, byte[] destination, int destOffset,
            int expected)
        {
            int read = 0;
            int written = 0;
            while (read < length && written < expected)
            {
                sbyte header = unchecked((sbyte)source[offset + read]);
                read++;
                if (header >= 0)
                {
                    int count = header + 1;
                    if (read + count > length)
                    {
                        count = Math.Max(0, length - read);
                    }

                    for (int i = 0; i < count && written < expected; i++)
                    {
                        destination[destOffset + written] = source[offset + read + i];
                        written++;
                    }

                    read += count;
                }
                else if (header != -128)
                {
                    int count = 1 - header;
                    if (read >= length)
                    {
                        break;
                    }

                    byte value = source[offset + read];
                    read++;
                    for (int i = 0; i < count && written < expected; i++)
                    {
                        destination[destOffset + written] = value;
                        written++;
                    }
                }
            }

            return written;
        }

        /// <summary>
        /// PSD 的 ZIP 数据是 zlib 容器；这里跳过 2 字节头用 DeflateStream 解压，
        /// 以便在只有 .NET Standard 2.1 的 Unity 里也能工作。
        /// </summary>
        private static byte[] Inflate(byte[] data, int offset, int length, int expected)
        {
            if (length <= 2)
            {
                throw new PsdParseException("ZIP 数据长度不足", offset);
            }

            byte[] result = new byte[expected];
            using (var stream = new MemoryStream(data, offset + 2, length - 2, false))
            using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < expected)
                {
                    int chunk = deflate.Read(result, read, expected - read);
                    if (chunk <= 0)
                    {
                        break;
                    }

                    read += chunk;
                }
            }

            return result;
        }

        /// <summary>
        /// 还原预测编码。每个通道的第一个样本原样保留，其余样本累加前一个样本，
        /// 行与行之间互不影响。16 位按大端样本累加，32 位还要把“按平面打包”的
        /// 字节重新交织回每个样本的 4 个字节。
        /// </summary>
        public static void UndoPrediction(byte[] data, int rowBytes, int height, int bytesPerSample)
        {
            if (bytesPerSample >= 4)
            {
                Accumulate(data, rowBytes, height, 1);
                Deinterleave32(data, rowBytes, height);
                return;
            }

            Accumulate(data, rowBytes, height, bytesPerSample > 1 ? 2 : 1);


        }

        private static void Accumulate(byte[] data, int rowBytes, int height, int sampleBytes)
        {
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * rowBytes;
                if (sampleBytes == 1)
                {
                    for (int i = 1; i < rowBytes; i++)
                    {
                        int index = rowStart + i;
                        data[index] = unchecked((byte)(data[index] + data[index - 1]));
                    }

                    continue;
                }

                // 16 位样本按大端读取后再相加，字节序保持不变
                for (int i = 2; i < rowBytes; i += 2)
                {
                    int high = rowStart + i;
                    int low = high + 1;
                    int value = ((data[high] << 8) | data[low]) + ((data[high - 2] << 8) | data[high - 1]);
                    data[high] = unchecked((byte)(value >> 8));
                    data[low] = unchecked((byte)value);
                }
            }
        }


        /// <summary>
        /// 32 位通道在差分前把每个 4 字节样本拆成 4 个平面存放
        /// （"12341234" 变成 "111222333"），这里按行还原成交织形式。
        /// </summary>
        private static void Deinterleave32(byte[] data, int rowBytes, int height)
        {
            int width = rowBytes / 4;
            var row = new byte[rowBytes];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * rowBytes;
                for (int pixel = 0; pixel < width; pixel++)
                {
                    for (int plane = 0; plane < 4; plane++)
                    {
                        row[pixel * 4 + plane] = data[rowStart + plane * width + pixel];
                    }
                }

                System.Array.Copy(row, 0, data, rowStart, rowBytes);
            }
        }

    }
}
