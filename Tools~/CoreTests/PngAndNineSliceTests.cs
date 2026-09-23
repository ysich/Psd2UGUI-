using System;
using System.Collections.Generic;
using System.Text;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Imaging;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    /// <summary>PNG 编码器：结构合法 + 像素无损。</summary>
    public class PngEncoderTests
    {
        [Fact]
        public void 编码结果是合法的RGBA32文件()
        {
            Bitmap bitmap = Bitmap.CreateFilled(3, 2, UiColor.FromBytes(10, 20, 30, 40));

            byte[] data = PngEncoder.Encode(bitmap);
            PngReader reader = PngReader.Decode(data);

            Assert.Equal(3, reader.Width);
            Assert.Equal(2, reader.Height);
            Assert.Equal(8, reader.BitDepth);
            Assert.Equal(6, reader.ColorType);
            Assert.True(reader.SawIend);
        }

        [Fact]
        public void 像素逐字节还原()
        {
            var bitmap = new Bitmap(4, 4);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bitmap.SetPixel(x, y, (byte)(x * 60), (byte)(y * 60), (byte)(x + y), (byte)(255 - x * 40 - y * 10));
                }
            }

            PngReader reader = PngReader.Decode(PngEncoder.Encode(bitmap));

            Assert.Equal(bitmap.Pixels, reader.Pixels);
        }

        [Fact]
        public void 带渐变的图会用到多种滤波()
        {
            var bitmap = new Bitmap(64, 8);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    bitmap.SetPixel(x, y, (byte)(x * 4), (byte)(y * 30), 128, 255);
                }
            }

            PngReader reader = PngReader.Decode(PngEncoder.Encode(bitmap));

            Assert.Equal(bitmap.Pixels, reader.Pixels);
            // 至少证明不是“永远写 0 号滤波”
            Assert.Contains(reader.RowFilters, filter => filter != 0);
        }

        [Fact]
        public void 纯色图压缩后远小于原始数据()
        {
            Bitmap bitmap = Bitmap.CreateFilled(256, 256, UiColor.FromBytes(255, 255, 255, 255));

            byte[] data = PngEncoder.Encode(bitmap);

            Assert.True(data.Length < bitmap.Pixels.Length / 50, "压缩后 " + data.Length + " 字节，太大");
        }

        [Fact]
        public void Crc32与Adler32符合标准向量()
        {
            byte[] check = Encoding.ASCII.GetBytes("123456789");
            Assert.Equal(0xCBF43926u, PngEncoder.Crc32(check));

            byte[] wikipedia = Encoding.ASCII.GetBytes("Wikipedia");
            Assert.Equal(0x11E60398u, PngEncoder.Adler32(wikipedia));
        }

        [Fact]
        public void 尺寸非法时抛异常()
        {
            Assert.Throws<ArgumentNullException>(() => PngEncoder.Encode(null));
            Assert.Throws<ArgumentException>(() => PngEncoder.Encode(new Bitmap(0, 4)));
            Assert.Throws<ArgumentException>(() => PngEncoder.EncodeRgba(2, 2, new byte[4]));
        }
    }

    /// <summary>九宫检测：固定边框、圆角、渐变、无边框、去空。</summary>
    public class NineSliceDetectorTests
    {
        [Fact]
        public void 纯色图判成无需九宫()
        {
            Bitmap bitmap = Bitmap.CreateFilled(16, 16, UiColor.FromBytes(255, 0, 0, 255));

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.True(result.IsUniform);
            Assert.False(result.IsSliceable);
            Assert.Equal(16, result.Sprite.Width);
            Assert.Contains("纯色", result.Reason);
        }

        [Fact]
        public void 固定边框图判出等宽边框()
        {
            Bitmap bitmap = Frame(20, 20, 2, 255, 0, 0);

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.True(result.IsSliceable);
            Assert.Equal(new UiBorder(2, 2, 2, 2), result.Border);
            // 中间被压成 1 像素：2 + 1 + 2
            Assert.Equal(5, result.Sprite.Width);
            Assert.Equal(5, result.Sprite.Height);
            AssertReconstructs(bitmap, result, 2, 2, 2, 2);
        }

        [Fact]
        public void 不等宽边框也能判出来()
        {
            // 左右各 4 像素、上下各 3 像素的框
            Bitmap bitmap = Frame(24, 24, 4, 3, 4, 3, 255, 0, 0);

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.True(result.IsSliceable);
            Assert.Equal(new UiBorder(4, 3, 4, 3), result.Border);
            AssertReconstructs(bitmap, result, 4, 3, 4, 3);
        }

        [Fact]
        public void 圆角矩形判出小于等于半径的边框()
        {
            Bitmap bitmap = RoundedRect(24, 6, 0, 128, 255);

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.True(result.IsSliceable);
            Assert.Equal(result.Border.Left, result.Border.Right);
            Assert.Equal(result.Border.Top, result.Border.Bottom);
            Assert.InRange(result.Border.Left, 1, 6);
            AssertReconstructs(bitmap, result, result.Border.Left, result.Border.Top, result.Border.Right,
                result.Border.Bottom);
        }

        [Fact]
        public void 中间是渐变的图放弃九宫()
        {
            // 上下有固定的 2 像素边框，中间是横向渐变：横向拉伸会糊，必须放弃
            var bitmap = new Bitmap(20, 20);
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    bool border = y < 2 || y >= 18;
                    byte value = (byte)(x * 12);
                    bitmap.SetPixel(x, y, border ? (byte)255 : value, border ? (byte)255 : (byte)0, 0, 255);
                }
            }

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.False(result.IsSliceable);
            Assert.Equal(20, result.Sprite.Width);
            Assert.Contains("不是纯色", result.Reason);
        }

        [Fact]
        public void 只有上下边框的纯色横条可以九宫()
        {
            // 上下各 2 行描边、中间纯色的横条：左右不需要边框
            var bitmap = new Bitmap(20, 10);
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    bool border = y < 2 || y >= 8;
                    bitmap.SetPixel(x, y, border ? (byte)255 : (byte)0, border ? (byte)0 : (byte)200, 0, 255);
                }
            }

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.True(result.IsSliceable);
            Assert.Equal(new UiBorder(0, 2, 0, 2), result.Border);
            Assert.Equal(1, result.Sprite.Width);
            Assert.Equal(5, result.Sprite.Height);
            AssertReconstructs(bitmap, result, 0, 2, 0, 2);
        }

        [Fact]
        public void 只在字符间隙找到纯色的文字图不能九宫()
        {
            // 中间那几列透明间隙看起来可拉伸，但左右两块的图案是斜纹，随行变化，
            // 纵向拉伸会糊，必须拒绝
            var bitmap = new Bitmap(20, 20);
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    bool gap = x >= 8 && x < 12;
                    bool ink = !gap && (x + y) % 5 == 0;
                    bitmap.SetPixel(x, y, 255, 255, 255, ink ? (byte)255 : (byte)0);
                }
            }

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.False(result.IsSliceable);
            Assert.Equal(20, result.Sprite.Width);
            Assert.Contains("放弃九宫", result.Reason);
        }

        [Fact]
        public void 没有规律可言的图按普通图导出()
        {
            var bitmap = new Bitmap(16, 16);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    bitmap.SetPixel(x, y, (byte)(x * 13), (byte)(y * 7), (byte)((x * y) % 251), 255);
                }
            }

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.False(result.IsSliceable);
            Assert.Contains("未检测到边框", result.Reason);
        }

        [Fact]
        public void 先去空边再判九宫()
        {
            var bitmap = new Bitmap(30, 30);
            Bitmap frame = Frame(20, 20, 2, 0, 255, 0);
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    int source = (y * 20 + x) * 4;
                    bitmap.SetPixel(5 + x, 5 + y, frame.Pixels[source], frame.Pixels[source + 1],
                        frame.Pixels[source + 2], frame.Pixels[source + 3]);
                }
            }

            NineSliceResult result = NineSliceDetector.Detect(bitmap);

            Assert.Equal(new UiRect(5d, 5d, 20d, 20d), result.SourceRect);
            Assert.Equal(new UiBorder(2, 2, 2, 2), result.Border);
            Assert.Equal(5, result.Sprite.Width);
        }

        [Fact]
        public void 整张透明的图返回空()
        {
            NineSliceResult result = NineSliceDetector.Detect(new Bitmap(8, 8));

            Assert.Null(result.Sprite);
            Assert.False(result.IsSliceable);
            Assert.Contains("透明", result.Reason);
        }

        [Fact]
        public void 关闭最小化时保留原尺寸()
        {
            Bitmap bitmap = Frame(20, 20, 2, 255, 0, 0);

            var options = new NineSliceOptions { Minimize = false };
            NineSliceResult result = NineSliceDetector.Detect(bitmap, options);

            Assert.Equal(20, result.Sprite.Width);
            Assert.Equal(new UiBorder(2, 2, 2, 2), result.Border);
        }

        [Fact]
        public void 空图直接返回()
        {
            Assert.Null(NineSliceDetector.Detect(null).Sprite);
            Assert.Null(NineSliceDetector.Detect(new Bitmap(0, 0)).Sprite);
        }

        /// <summary>
        /// 九宫的正确性判据：用「Sprite + Border」按原始尺寸还原，
        /// 结果必须和原图逐像素一致 —— 说明可拉伸区确实是均匀的。
        /// </summary>
        private static void AssertReconstructs(Bitmap original, NineSliceResult result, int left, int top, int right,
            int bottom)
        {
            Bitmap sprite = result.Sprite;
            var restored = new Bitmap(original.Width, original.Height);
            for (int y = 0; y < original.Height; y++)
            {
                int sourceY = Map(y, top, bottom, sprite.Height, original.Height);
                for (int x = 0; x < original.Width; x++)
                {
                    int sourceX = Map(x, left, right, sprite.Width, original.Width);
                    int source = (sourceY * sprite.Width + sourceX) * 4;
                    int target = (y * original.Width + x) * 4;
                    restored.Pixels[target] = sprite.Pixels[source];
                    restored.Pixels[target + 1] = sprite.Pixels[source + 1];
                    restored.Pixels[target + 2] = sprite.Pixels[source + 2];
                    restored.Pixels[target + 3] = sprite.Pixels[source + 3];
                }
            }

            Assert.Equal(original.Pixels, restored.Pixels);
        }

        private static int Map(int position, int border, int opposite, int spriteLength, int targetLength)
        {
            if (position < border)
            {
                return position;
            }

            if (position >= targetLength - opposite)
            {
                return spriteLength - (targetLength - position);
            }

            return border;
        }

        /// <summary>实心圆角矩形（硬边，无抗锯齿）。</summary>
        private static Bitmap RoundedRect(int size, int radius, byte r, byte g, byte b)
        {
            var bitmap = new Bitmap(size, size);
            double near = radius;
            double far = size - 1 - radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = Math.Max(Math.Max(near - x, x - far), 0d);
                    double dy = Math.Max(Math.Max(near - y, y - far), 0d);
                    bool inside = dx * dx + dy * dy <= radius * radius;
                    bitmap.SetPixel(x, y, r, g, b, inside ? (byte)255 : (byte)0);
                }
            }

            return bitmap;
        }

        /// <summary>四边等宽的纯色边框，中间留空。</summary>
        private static Bitmap Frame(int width, int height, int thickness, byte r, byte g, byte b)
        {
            return Frame(width, height, thickness, thickness, thickness, thickness, r, g, b);
        }

        /// <summary>可以分别指定四边宽度的纯色边框。</summary>
        private static Bitmap Frame(int width, int height, int left, int top, int right, int bottom, byte r, byte g,
            byte b)
        {
            var bitmap = new Bitmap(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x < left || y < top || x >= width - right || y >= height - bottom;
                    bitmap.SetPixel(x, y, r, g, b, border ? (byte)255 : (byte)0);
                }
            }

            return bitmap;
        }
    }
}
