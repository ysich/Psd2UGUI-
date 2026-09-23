using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Psd2Ugui.Core.Psd;
using Xunit;
using Psd2Ugui.Testing;

namespace Psd2Ugui.CoreTests
{
    public class PsdChannelCodecTests
    {
        /// <summary>PackBits 字面量行：表头 count-1 后跟原始字节。</summary>
        private static byte[] LiteralRow(params byte[] values)
        {
            var row = new List<byte> { (byte)(values.Length - 1) };
            row.AddRange(values);
            return row.ToArray();
        }

        [Fact]
        public void Raw通道直接拷贝并按期望长度补齐()
        {
            byte[] payload = { 10, 20, 30, 40 };
            byte[] result = PsdChannelCodec.Decode(payload, 0, payload.Length, PsdCompression.Raw, 2, 2, 1, false);

            Assert.Equal(new byte[] { 10, 20, 30, 40 }, result);
        }

        [Fact]
        public void Raw通道数据不足时用0补齐而不是越界()
        {
            byte[] payload = { 10, 20 };
            byte[] result = PsdChannelCodec.Decode(payload, 0, payload.Length, PsdCompression.Raw, 2, 2, 1, false);

            Assert.Equal(4, result.Length);
            Assert.Equal(10, result[0]);
            Assert.Equal(0, result[3]);
        }

        [Fact]
        public void Rle通道按行长度表解码PackBits()
        {
            // 2x2 的 8 位通道：两行，每行 2 字节
            byte[] row0 = LiteralRow(0x10, 0x20);
            byte[] row1 = LiteralRow(0x30, 0x40);

            var payload = new List<byte>();
            PsdFixtureBuilder.WriteU16(payload, (ushort)row0.Length);
            PsdFixtureBuilder.WriteU16(payload, (ushort)row1.Length);
            payload.AddRange(row0);
            payload.AddRange(row1);

            byte[] result = PsdChannelCodec.Decode(payload.ToArray(), 0, payload.Count, PsdCompression.Rle, 2, 2, 1, false);

            Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40 }, result);
        }

        [Fact]
        public void Rle支持重复字节的压缩包()
        {
            // 4 像素一行：PackBits 0xFF 表示后随 1 字节重复 2 次
            var row = new byte[] { 0xFF, 0x7F, 0x01, 0xAA, 0xBB };
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteU16(payload, (ushort)row.Length);
            payload.AddRange(row);

            byte[] result = PsdChannelCodec.Decode(payload.ToArray(), 0, payload.Count, PsdCompression.Rle, 4, 1, 1, false);

            Assert.Equal(new byte[] { 0x7F, 0x7F, 0xAA, 0xBB }, result);
        }

        [Fact]
        public void Rle行长度表不完整时抛解析异常()
        {
            byte[] payload = { 0x00 };
            Assert.Throws<PsdParseException>(() =>
                PsdChannelCodec.Decode(payload, 0, payload.Length, PsdCompression.Rle, 2, 2, 1, false));
        }

        [Fact]
        public void Zip通道跳过zlib头后用Deflate解压()
        {
            byte[] expected = { 1, 2, 3, 4, 5, 6 };
            byte[] payload = Zlib(expected);

            byte[] result = PsdChannelCodec.Decode(payload, 0, payload.Length, PsdCompression.Zip, 3, 2, 1, false);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void Zip结合预测时按行还原差分()
        {
            // 差值编码：每行第一个字节原样，其余存与前一字节的差
            byte[] deltas = { 10, 2, 2, 2, 100, 1, 2, 5 };
            byte[] payload = Zlib(deltas);

            byte[] result = PsdChannelCodec.Decode(payload, 0, payload.Length, PsdCompression.ZipWithPrediction, 4, 2, 1,
                false);

            Assert.Equal(new byte[] { 10, 12, 14, 16, 100, 101, 103, 108 }, result);
        }

        [Fact]
        public void Zip结合预测的16位通道按样本宽度解码()
        {
            byte[] deltas = { 1, 0, 0, 16, 0, 5, 2, 0, 255, 255, 0, 1 };
            byte[] payload = Zlib(deltas);

            byte[] result = PsdChannelCodec.Decode(payload, 0, payload.Length, PsdCompression.ZipWithPrediction, 3, 2, 2,
                false);

            Assert.Equal(new byte[] { 1, 0, 1, 16, 1, 21, 2, 0, 1, 255, 2, 0 }, result);
        }


        [Fact]
        public void UndoPrediction逐行独立累加()
        {
            byte[] data = { 10, 2, 2, 2, 100, 1, 2, 5 };
            PsdChannelCodec.UndoPrediction(data, 4, 2, 1);

            Assert.Equal(new byte[] { 10, 12, 14, 16, 100, 101, 103, 108 }, data);
        }

        [Fact]
        public void UndoPrediction对16位样本按大端累加()
        {
            byte[] data = { 1, 0, 0, 16, 0, 5, 2, 0, 255, 255, 0, 1 };
            PsdChannelCodec.UndoPrediction(data, 6, 2, 2);

            // 期望值与 psd-tools 的 decode_prediction(depth=16) 一致
            Assert.Equal(new byte[] { 1, 0, 1, 16, 1, 21, 2, 0, 1, 255, 2, 0 }, data);
        }

        [Fact]
        public void UndoPrediction对32位样本先累加再还原字节交织()
        {
            var data = new byte[16];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i + 1);
            }

            PsdChannelCodec.UndoPrediction(data, 8, 2, 4);

            // 期望值与 psd-tools 的 decode_prediction(depth=32) 一致
            Assert.Equal(new byte[] { 1, 6, 15, 28, 3, 10, 21, 36, 9, 30, 55, 84, 19, 42, 69, 100 }, data);
        }



        [Fact]
        public void 不支持的压缩方式抛解析异常()
        {
            byte[] payload = { 1, 2, 3, 4 };
            Assert.Throws<PsdParseException>(() =>
                PsdChannelCodec.Decode(payload, 0, payload.Length, 42, 2, 2, 1, false));
        }

        [Fact]
        public void UnpackBits遇到行尾会停止写入()
        {
            byte[] source = { 0x05, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
            var destination = new byte[4];
            int written = PsdChannelCodec.UnpackBits(source, 0, source.Length, destination, 0, 4);

            Assert.Equal(4, written);
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, destination);
        }

        private static byte[] Zlib(byte[] raw)
        {
            var result = new List<byte> { 0x78, 0x9C };
            using (var compressed = new MemoryStream())
            {
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                {
                    deflate.Write(raw, 0, raw.Length);
                }

                result.AddRange(compressed.ToArray());
            }

            return result.ToArray();
        }
    }
}
