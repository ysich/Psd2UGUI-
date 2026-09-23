using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Psd;

namespace Psd2Ugui.Core.Imaging
{
    /// <summary>位图合成的可调项。</summary>
    public sealed class RasterizeOptions
    {
        /// <summary>是否把隐藏图层也画出来，默认跳过（与 PS 面板一致）。</summary>
        public bool IncludeHiddenLayers;

        /// <summary>低于该覆盖率的图层视为空图层，直接跳过，避免导出全透明贴图。</summary>
        public double EmptyLayerCoverage = 0d;
    }

    /// <summary>
    /// 图层 → 位图。读取图层自己的通道得到 RGBA，再依次叠加用户蒙版与裁剪关系。
    /// 输出的 alpha 是图层自身的不透明信息，不预乘图层不透明度——
    /// 不透明度留给 Prefab 的 Image.color.a，方便在 Unity 里继续调。
    /// </summary>
    public static class LayerRasterizer
    {
        /// <summary>把单个图层栅格化，得到与图层矩形同尺寸的 RGBA 位图。</summary>
        public static Bitmap Rasterize(PsdFile file, PsdLayer layer, IList<string> warnings = null,
            Bitmap clippingBase = null)
        {
            if (file == null || layer == null)
            {
                return null;
            }

            if (layer.IsGroup || layer.IsBoundingDivider)
            {
                return null;
            }

            int width = layer.Width;
            int height = layer.Height;
            if (width <= 0 || height <= 0)
            {
                return new Bitmap(0, 0);
            }

            int pixelCount = width * height;
            byte[] red;
            byte[] green;
            byte[] blue;
            if (!TryReadColorChannels(file, layer, pixelCount, warnings, out red, out green, out blue))
            {
                return null;
            }

            byte[] alpha = ReadAlpha(file, layer, pixelCount);

            var bitmap = new Bitmap(width, height);
            for (int i = 0; i < pixelCount; i++)
            {
                int index = i * 4;
                bitmap.Pixels[index] = red[i];
                bitmap.Pixels[index + 1] = green[i];
                bitmap.Pixels[index + 2] = blue[i];
                bitmap.Pixels[index + 3] = alpha == null ? (byte)255 : alpha[i];
            }

            ApplyUserMask(file, layer, bitmap, warnings);
            ApplyClipping(bitmap, clippingBase);
            return bitmap;
        }

        /// <summary>
        /// 按 PSD 记录顺序栅格化整份文档（自下而上），并把可见图层合成到画布上。
        /// 用于预览图、缩略图以及需要“整组一起导出”的场景。
        /// </summary>
        public static Bitmap Composite(PsdFile file, RasterizeOptions options = null, IList<string> warnings = null)
        {
            if (file == null || file.Width <= 0 || file.Height <= 0)
            {
                return null;
            }

            options = options ?? new RasterizeOptions();
            var canvas = new Bitmap(file.Width, file.Height);
            foreach (LayerDraw draw in EnumerateDrawCalls(file, options, warnings))
            {
                canvas.DrawOver(draw.Bitmap, (int)draw.Layer.Rect.X, (int)draw.Layer.Rect.Y, draw.Opacity);

            }

            return canvas;
        }

        /// <summary>一次遍历里需要画的一张图：图层 + 位图 + 该图层的不透明度。</summary>
        public sealed class LayerDraw
        {
            public PsdLayer Layer;
            public Bitmap Bitmap;
            public double Opacity = 1d;
        }

        /// <summary>
        /// 按记录顺序枚举“可绘制的图层”（跳过隐藏层、空层、分组），
        /// 并顺带处理裁剪层与它下面的基准层。
        /// </summary>
        public static IEnumerable<LayerDraw> EnumerateDrawCalls(PsdFile file, RasterizeOptions options,
            IList<string> warnings = null)
        {
            options = options ?? new RasterizeOptions();
            Bitmap clippingBase = null;
            for (int i = 0; i < file.Layers.Count; i++)
            {
                PsdLayer layer = file.Layers[i];
                if (layer.IsGroup || layer.IsBoundingDivider)
                {
                    // 分组本身不产生像素，但分组会打断裁剪链
                    clippingBase = null;
                    continue;
                }

                if (!layer.Visible && !options.IncludeHiddenLayers)
                {
                    continue;
                }

                Bitmap bitmap = Rasterize(file, layer, warnings, layer.Clipping ? clippingBase : null);
                if (bitmap == null || bitmap.IsEmpty)
                {
                    continue;
                }

                double opacity = layer.Opacity / 255d;
                if (bitmap.IsFullyTransparent())
                {
                    continue;
                }

                if (bitmap.AlphaCoverage() <= options.EmptyLayerCoverage)
                {
                    continue;
                }

                // 裁剪层自己不作为后面的基准层
                if (!layer.Clipping)
                {
                    clippingBase = bitmap;
                }

                yield return new LayerDraw { Layer = layer, Bitmap = bitmap, Opacity = opacity };
            }
        }

        private static bool TryReadColorChannels(PsdFile file, PsdLayer layer, int pixelCount,
            IList<string> warnings, out byte[] red, out byte[] green, out byte[] blue)
        {
            PsdColorMode mode = file.Mode;

            if (mode == PsdColorMode.Rgb)
            {
                byte[] r = ReadChannel(file, layer, PsdChannelId.Red, pixelCount);
                byte[] g = ReadChannel(file, layer, PsdChannelId.Green, pixelCount);
                byte[] b = ReadChannel(file, layer, PsdChannelId.Blue, pixelCount);
                if (r == null && g == null && b == null)
                {
                    return FillFromSolidColor(layer, pixelCount, warnings, out red, out green, out blue);
                }

                red = r ?? Filled(pixelCount, 0);
                green = g ?? Filled(pixelCount, 0);
                blue = b ?? Filled(pixelCount, 0);
                return true;
            }

            if (mode == PsdColorMode.Grayscale || mode == PsdColorMode.Indexed)
            {
                byte[] gray = ReadChannel(file, layer, PsdChannelId.Grayscale, pixelCount);
                if (gray == null)
                {
                    return FillFromSolidColor(layer, pixelCount, warnings, out red, out green, out blue);
                }

                if (mode == PsdColorMode.Indexed && warnings != null)
                {
                    Warn(warnings, "索引色图层 '" + layer.DisplayName + "' 缺少调色板，按灰度处理");
                }

                red = gray;
                green = gray;
                blue = gray;
                return true;
            }

            if (mode == PsdColorMode.Cmyk)
            {
                byte[] c = ReadChannel(file, layer, 0, pixelCount);
                byte[] m = ReadChannel(file, layer, 1, pixelCount);
                byte[] y = ReadChannel(file, layer, 2, pixelCount);
                byte[] k = ReadChannel(file, layer, 3, pixelCount);
                if (c == null)
                {
                    return FillFromSolidColor(layer, pixelCount, warnings, out red, out green, out blue);
                }

                m = m ?? Filled(pixelCount, 0);
                y = y ?? Filled(pixelCount, 0);
                k = k ?? Filled(pixelCount, 0);
                red = new byte[pixelCount];
                green = new byte[pixelCount];
                blue = new byte[pixelCount];
                for (int i = 0; i < pixelCount; i++)
                {
                    // 简易 CMYK → RGB，足够满足 UI 素材的近似需求
                    red[i] = ClampToByte(255 - Math.Min(255, c[i] + k[i]));
                    green[i] = ClampToByte(255 - Math.Min(255, m[i] + k[i]));
                    blue[i] = ClampToByte(255 - Math.Min(255, y[i] + k[i]));
                }

                return true;
            }

            if (warnings != null)
            {
                Warn(warnings, "颜色模式 " + mode + " 暂不支持栅格化，图层 '" + layer.DisplayName + "' 已跳过");
            }

            red = green = blue = null;
            return false;
        }

        private static bool FillFromSolidColor(PsdLayer layer, int pixelCount, IList<string> warnings,
            out byte[] red, out byte[] green, out byte[] blue)
        {
            if (layer.HasSolidFill)
            {
                red = Filled(pixelCount, Bitmap.ToByte(layer.SolidFill.R));
                green = Filled(pixelCount, Bitmap.ToByte(layer.SolidFill.G));
                blue = Filled(pixelCount, Bitmap.ToByte(layer.SolidFill.B));
                return true;
            }

            if (warnings != null)
            {
                Warn(warnings, "图层 '" + layer.DisplayName + "' 没有像素通道，已跳过");
            }

            red = green = blue = null;
            return false;
        }

        private static byte[] ReadAlpha(PsdFile file, PsdLayer layer, int pixelCount)
        {
            return ReadChannel(file, layer, PsdChannelId.Transparency, pixelCount);
        }

        private static void ApplyUserMask(PsdFile file, PsdLayer layer, Bitmap bitmap, IList<string> warnings)
        {
            if (!layer.HasMask)
            {
                return;
            }

            byte[] mask = ReadChannel(file, layer, PsdChannelId.UserMask, bitmap.Width * bitmap.Height);
            if (mask == null)
            {
                return;
            }

            for (int i = 0; i < mask.Length; i++)
            {
                int index = i * 4 + 3;
                bitmap.Pixels[index] = (byte)(bitmap.Pixels[index] * mask[i] / 255);
            }
        }

        /// <summary>裁剪层只显示在基准层的形状里：按基准层的 alpha 再乘一次。</summary>
        private static void ApplyClipping(Bitmap bitmap, Bitmap clippingBase)
        {
            if (clippingBase == null || clippingBase.Width != bitmap.Width || clippingBase.Height != bitmap.Height)
            {
                return;
            }

            for (int i = 3; i < bitmap.Pixels.Length; i += 4)
            {
                bitmap.Pixels[i] = (byte)(bitmap.Pixels[i] * clippingBase.Pixels[i] / 255);
            }
        }

        /// <summary>按通道号读取并归一化成每像素 1 字节；通道不存在时返回 null。</summary>
        public static byte[] ReadChannel(PsdFile file, PsdLayer layer, int channelId, int pixelCount)
        {
            PsdChannelInfo channel = layer.FindChannel(channelId);
            if (channel == null || channel.Length <= 2)
            {
                return null;
            }

            if (file.BitDepth == 1)

            {
                return ReadOneBitChannel(file, layer, channel, pixelCount);
            }

            byte[] raw = file.ReadChannel(layer, channel);
            int bytesPerSample = layer.BytesPerSample > 0 ? layer.BytesPerSample : 1;
            if (raw == null || raw.Length < pixelCount * bytesPerSample)
            {
                return null;
            }

            if (bytesPerSample == 1)
            {
                return raw;
            }

            var result = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                result[i] = SampleToByte(raw, i * bytesPerSample, bytesPerSample);
            }

            return result;
        }

        private static byte[] ReadOneBitChannel(PsdFile file, PsdLayer layer, PsdChannelInfo channel, int pixelCount)
        {
            byte[] raw = file.ReadChannel(layer, channel);
            if (raw == null)
            {
                return null;
            }

            int width = layer.Width;
            int rowBytes = (width + 7) / 8;
            var result = new byte[pixelCount];
            for (int y = 0; y < layer.Height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int byteIndex = y * rowBytes + x / 8;
                    if (byteIndex >= raw.Length)
                    {
                        break;
                    }

                    // 1 位通道里 1 表示白
                    bool set = (raw[byteIndex] & (0x80 >> (x % 8))) != 0;
                    result[y * width + x] = set ? (byte)255 : (byte)0;
                }
            }

            return result;
        }

        private static byte SampleToByte(byte[] data, int offset, int bytesPerSample)
        {
            if (bytesPerSample == 2)
            {
                int value = (data[offset] << 8) | data[offset + 1];
                return (byte)Math.Round(value * 255d / 65535d, MidpointRounding.AwayFromZero);
            }


            // 32 位是 0~1 的浮点样本，大端存储
            byte[] slice = new byte[4];
            slice[0] = data[offset + 3];
            slice[1] = data[offset + 2];
            slice[2] = data[offset + 1];
            slice[3] = data[offset];
            float sample = BitConverter.ToSingle(slice, 0);
            if (sample <= 0f)
            {
                return 0;
            }

            return sample >= 1f ? (byte)255 : (byte)Math.Round(sample * 255f);
        }

        private static byte[] Filled(int count, byte value)
        {
            var result = new byte[count];
            if (value != 0)
            {
                for (int i = 0; i < count; i++)
                {
                    result[i] = value;
                }
            }

            return result;
        }

        private static byte ClampToByte(int value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= 255 ? (byte)255 : (byte)value;
        }

        private static void Warn(IList<string> warnings, string message)
        {
            if (warnings != null && !warnings.Contains(message))
            {
                warnings.Add(message);
            }
        }
    }
}
