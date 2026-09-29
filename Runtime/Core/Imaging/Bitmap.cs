using System;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Imaging
{
    /// <summary>
    /// 32 位 RGBA 位图（每通道 1 字节，直通 alpha，未预乘）。
    /// 图层位图的尺寸就是图层在 PSD 里的矩形，坐标原点在图层左上角。
    /// </summary>
    public sealed class Bitmap
    {
        public Bitmap(int width, int height)
        {
            Width = width < 0 ? 0 : width;
            Height = height < 0 ? 0 : height;
            Pixels = new byte[Width * Height * 4];
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        /// <summary>RGBA 顺序，长度固定为 Width * Height * 4。</summary>
        public byte[] Pixels { get; private set; }

        public bool IsEmpty
        {
            get { return Width <= 0 || Height <= 0; }
        }

        public int ByteCount
        {
            get { return Pixels.Length; }
        }

        public static Bitmap CreateFilled(int width, int height, UiColor color)
        {
            var bitmap = new Bitmap(width, height);
            byte r = ToByte(color.R);
            byte g = ToByte(color.G);
            byte b = ToByte(color.B);
            byte a = ToByte(color.A);
            for (int i = 0; i < bitmap.Pixels.Length; i += 4)
            {
                bitmap.Pixels[i] = r;
                bitmap.Pixels[i + 1] = g;
                bitmap.Pixels[i + 2] = b;
                bitmap.Pixels[i + 3] = a;
            }

            return bitmap;
        }

        public static byte ToByte(double value)
        {
            if (value <= 0d)
            {
                return 0;
            }

            if (value >= 1d)
            {
                return 255;
            }

            return (byte)Math.Round(value * 255d);
        }

        public bool Contains(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
        {
            if (!Contains(x, y))
            {
                return;
            }

            int index = (y * Width + x) * 4;
            Pixels[index] = r;
            Pixels[index + 1] = g;
            Pixels[index + 2] = b;
            Pixels[index + 3] = a;
        }

        public byte AlphaAt(int x, int y)
        {
            if (!Contains(x, y))
            {
                return 0;
            }

            return Pixels[(y * Width + x) * 4 + 3];
        }

        public Bitmap Clone()
        {
            var copy = new Bitmap(Width, Height);
            Array.Copy(Pixels, copy.Pixels, Pixels.Length);
            return copy;
        }

        public Bitmap Crop(int x, int y, int width, int height)
        {
            var result = new Bitmap(width, height);
            for (int row = 0; row < height; row++)
            {
                int sourceY = y + row;
                if (sourceY < 0 || sourceY >= Height)
                {
                    continue;
                }

                for (int column = 0; column < width; column++)
                {
                    int sourceX = x + column;
                    if (sourceX < 0 || sourceX >= Width)
                    {
                        continue;
                    }

                    int source = (sourceY * Width + sourceX) * 4;
                    int target = (row * width + column) * 4;
                    result.Pixels[target] = Pixels[source];
                    result.Pixels[target + 1] = Pixels[source + 1];
                    result.Pixels[target + 2] = Pixels[source + 2];
                    result.Pixels[target + 3] = Pixels[source + 3];
                }
            }

            return result;
        }

        /// <summary>
        /// 把图贴到一张补了透明边的画布上（原图左上角落在给定的空边之后），只搬字节不做混合。
        /// 去空边导出的图要按图层矩形原样铺回去时用得上。
        /// </summary>
        public static Bitmap Pad(Bitmap bitmap, int left, int top, int right, int bottom)
        {
            if (bitmap == null)
            {
                return new Bitmap(0, 0);
            }

            int originX = left < 0 ? 0 : left;
            int originY = top < 0 ? 0 : top;
            var result = new Bitmap(originX + bitmap.Width + (right < 0 ? 0 : right),
                originY + bitmap.Height + (bottom < 0 ? 0 : bottom));
            for (int y = 0; y < bitmap.Height; y++)
            {
                Array.Copy(bitmap.Pixels, y * bitmap.Width * 4, result.Pixels,
                    ((originY + y) * result.Width + originX) * 4, bitmap.Width * 4);
            }

            return result;
        }

        /// <summary>整张图是否完全透明（空图层，导出时可以整个跳过）。</summary>
        public bool IsFullyTransparent()
        {
            for (int i = 3; i < Pixels.Length; i += 4)
            {
                if (Pixels[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>整体乘一个透明度系数（图层不透明度、组不透明度都走这里）。</summary>
        public void MultiplyAlpha(double factor)
        {
            if (factor >= 1d)
            {
                return;
            }

            if (factor <= 0d)
            {
                for (int i = 3; i < Pixels.Length; i += 4)
                {
                    Pixels[i] = 0;
                }

                return;
            }

            for (int i = 3; i < Pixels.Length; i += 4)
            {
                Pixels[i] = (byte)Math.Round(Pixels[i] * factor);
            }
        }

        /// <summary>
        /// 求非透明像素的包围盒（相对本图左上角），用于裁掉四周的空边。
        /// 全透明时返回 false。
        /// </summary>
        public bool TryGetContentBounds(out UiRect bounds, int alphaThreshold = 0)
        {
            int minX = Width;
            int minY = Height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (Pixels[(y * Width + x) * 4 + 3] <= alphaThreshold)
                    {
                        continue;
                    }

                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }

            if (maxX < 0)
            {
                bounds = UiRect.Empty;
                return false;
            }

            bounds = new UiRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        /// <summary>去空边：把四周完全透明的部分裁掉，没有内容时返回 null。</summary>
        public Bitmap TrimTransparent(out UiRect bounds, int alphaThreshold = 0)
        {
            if (!TryGetContentBounds(out bounds, alphaThreshold))
            {
                return null;
            }

            if (bounds.X == 0d && bounds.Y == 0d && bounds.Width == Width && bounds.Height == Height)
            {
                return this;
            }

            return Crop((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
        }

        /// <summary>非透明像素占比，用于诊断“这个图层是不是空的”。</summary>
        public double AlphaCoverage(int alphaThreshold = 0)
        {
            if (Pixels.Length == 0)
            {
                return 0d;
            }

            int count = 0;
            for (int i = 3; i < Pixels.Length; i += 4)
            {
                if (Pixels[i] > alphaThreshold)
                {
                    count++;
                }
            }

            return count / (double)(Width * Height);
        }

        /// <summary>
        /// 把另一张图按普通 alpha 混合叠到本图上。两张图都用直通 alpha。
        /// </summary>
        public void DrawOver(Bitmap source, int offsetX, int offsetY, double opacity = 1d)
        {
            if (source == null || source.IsEmpty || opacity <= 0d)
            {
                return;
            }

            for (int y = 0; y < source.Height; y++)
            {
                int targetY = offsetY + y;
                if (targetY < 0 || targetY >= Height)
                {
                    continue;
                }

                for (int x = 0; x < source.Width; x++)
                {
                    int targetX = offsetX + x;
                    if (targetX < 0 || targetX >= Width)
                    {
                        continue;
                    }

                    int sourceIndex = (y * source.Width + x) * 4;
                    int targetIndex = (targetY * Width + targetX) * 4;
                    BlendPixel(source, sourceIndex, Pixels, targetIndex, opacity);
                }
            }
        }

        private static void BlendPixel(Bitmap source, int sourceIndex, byte[] target, int targetIndex, double opacity)
        {
            double sourceAlpha = source.Pixels[sourceIndex + 3] / 255d * opacity;
            if (sourceAlpha <= 0d)
            {
                return;
            }

            double targetAlpha = target[targetIndex + 3] / 255d;
            double outAlpha = sourceAlpha + targetAlpha * (1d - sourceAlpha);
            if (outAlpha <= 0d)
            {
                target[targetIndex] = 0;
                target[targetIndex + 1] = 0;
                target[targetIndex + 2] = 0;
                target[targetIndex + 3] = 0;
                return;
            }

            for (int channel = 0; channel < 3; channel++)
            {
                double sourceValue = source.Pixels[sourceIndex + channel] / 255d;
                double targetValue = target[targetIndex + channel] / 255d;
                double blended = (sourceValue * sourceAlpha + targetValue * targetAlpha * (1d - sourceAlpha)) / outAlpha;
                target[targetIndex + channel] = ToByte(blended);
            }

            target[targetIndex + 3] = ToByte(outAlpha);
        }
    }
}
