using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Imaging;
using Psd2Ugui.Core.Psd;
using Xunit;
using Psd2Ugui.Testing;

namespace Psd2Ugui.CoreTests
{
    /// <summary>位图本身的算法：裁剪、包围盒、覆盖率、alpha 混合。</summary>
    public class BitmapTests
    {
        [Fact]
        public void 新建的位图是透明的()
        {
            var bitmap = new Bitmap(2, 1);

            Assert.Equal(2, bitmap.Width);
            Assert.Equal(1, bitmap.Height);
            Assert.Equal(8, bitmap.ByteCount);
            Assert.True(bitmap.IsFullyTransparent());
            Assert.Equal(0d, bitmap.AlphaCoverage());
            Assert.False(bitmap.IsEmpty);
        }

        [Fact]
        public void 尺寸为负时归零并标记为空()
        {
            var bitmap = new Bitmap(-3, 4);

            Assert.Equal(0, bitmap.Width);
            Assert.True(bitmap.IsEmpty);
            Assert.Empty(bitmap.Pixels);
        }

        [Fact]
        public void 纯色填充按RGBA顺序铺满()
        {
            Bitmap bitmap = Bitmap.CreateFilled(2, 2, UiColor.FromBytes(10, 20, 30, 200));

            Assert.Equal(new byte[] { 10, 20, 30, 200 }, Slice(bitmap, 0));
            Assert.Equal(new byte[] { 10, 20, 30, 200 }, Slice(bitmap, 3));
            Assert.Equal(200, bitmap.AlphaAt(1, 1));
            Assert.Equal(1d, bitmap.AlphaCoverage());
        }

        [Fact]
        public void 越界写入与读取都被忽略()
        {
            var bitmap = new Bitmap(2, 2);
            bitmap.SetPixel(-1, 0, 1, 2, 3, 4);
            bitmap.SetPixel(0, 2, 1, 2, 3, 4);

            Assert.Equal(0, bitmap.AlphaAt(-1, 0));
            Assert.Equal(0, bitmap.AlphaAt(0, 2));
            Assert.True(bitmap.IsFullyTransparent());
        }

        [Fact]
        public void 裁剪超出边界的部分补透明()
        {
            var bitmap = new Bitmap(2, 2);
            bitmap.SetPixel(0, 0, 1, 2, 3, 4);

            Bitmap cropped = bitmap.Crop(-1, -1, 3, 3);

            Assert.Equal(3, cropped.Width);
            Assert.Equal(3, cropped.Height);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Slice(cropped, 1 * 3 + 1));
            Assert.True(cropped.AlphaAt(0, 0) == 0);
        }

        [Fact]
        public void 包围盒只统计非透明像素()
        {
            var bitmap = new Bitmap(4, 4);
            bitmap.SetPixel(1, 1, 9, 9, 9, 255);
            bitmap.SetPixel(2, 3, 9, 9, 9, 255);

            Assert.True(bitmap.TryGetContentBounds(out UiRect bounds));
            Assert.Equal(new UiRect(1d, 1d, 2d, 3d), bounds);
            Assert.Equal(2d / 16d, bitmap.AlphaCoverage());
        }

        [Fact]
        public void 全透明时没有包围盒()
        {
            var bitmap = new Bitmap(3, 3);

            Assert.False(bitmap.TryGetContentBounds(out UiRect bounds));
            Assert.True(bounds.IsEmpty);
            Assert.Null(bitmap.TrimTransparent(out UiRect trimmedBounds));
            Assert.True(trimmedBounds.IsEmpty);
        }

        [Fact]
        public void 去空边裁掉四周透明区域()
        {
            var bitmap = new Bitmap(4, 4);
            for (int y = 1; y <= 2; y++)
            {
                for (int x = 1; x <= 2; x++)
                {
                    bitmap.SetPixel(x, y, 1, 2, 3, 255);
                }
            }

            Bitmap trimmed = bitmap.TrimTransparent(out UiRect bounds);

            Assert.Equal(new UiRect(1d, 1d, 2d, 2d), bounds);
            Assert.Equal(2, trimmed.Width);
            Assert.Equal(2, trimmed.Height);
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, Slice(trimmed, 0));
        }

        [Fact]
        public void 没有空边时返回自身()
        {
            Bitmap bitmap = Bitmap.CreateFilled(2, 2, UiColor.White);

            Bitmap trimmed = bitmap.TrimTransparent(out UiRect bounds);

            Assert.Same(bitmap, trimmed);
            Assert.Equal(new UiRect(0d, 0d, 2d, 2d), bounds);
        }

        [Fact]
        public void 覆盖率按阈值统计()
        {
            var bitmap = new Bitmap(1, 4);
            bitmap.SetPixel(0, 0, 0, 0, 0, 10);
            bitmap.SetPixel(0, 1, 0, 0, 0, 60);
            bitmap.SetPixel(0, 2, 0, 0, 0, 255);

            // 阈值 50 时只剩 60 与 255 两个像素算数
            Assert.Equal(0.5d, bitmap.AlphaCoverage(50));
            Assert.Equal(0.75d, bitmap.AlphaCoverage());

        }

        [Fact]
        public void 整体乘透明度系数()
        {
            var bitmap = new Bitmap(1, 2);
            bitmap.SetPixel(0, 0, 1, 2, 3, 255);
            bitmap.SetPixel(0, 1, 1, 2, 3, 100);

            bitmap.MultiplyAlpha(0.5d);

            Assert.Equal(128, bitmap.AlphaAt(0, 0));
            Assert.Equal(50, bitmap.AlphaAt(0, 1));

            Assert.Equal(new byte[] { 1, 2, 3 }, new[] { bitmap.Pixels[0], bitmap.Pixels[1], bitmap.Pixels[2] });
        }

        [Fact]
        public void 透明度系数为零时全透明()
        {
            var bitmap = new Bitmap(2, 1);
            bitmap.SetPixel(0, 0, 1, 2, 3, 255);
            bitmap.SetPixel(1, 0, 1, 2, 3, 255);

            bitmap.MultiplyAlpha(0d);

            Assert.True(bitmap.IsFullyTransparent());
        }

        [Fact]
        public void 不透明图层直接覆盖底色()
        {
            var canvas = new Bitmap(1, 1);
            canvas.SetPixel(0, 0, 0, 0, 255, 255);
            var source = new Bitmap(1, 1);
            source.SetPixel(0, 0, 255, 0, 0, 255);

            canvas.DrawOver(source, 0, 0);

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Slice(canvas, 0));
        }

        [Fact]
        public void 半透明图层按直通alpha公式混合()
        {
            var canvas = new Bitmap(1, 1);
            canvas.SetPixel(0, 0, 0, 0, 255, 255);
            var source = new Bitmap(1, 1);
            source.SetPixel(0, 0, 255, 0, 0, 128);

            canvas.DrawOver(source, 0, 0);

            // out = src*sa + dst*(1-sa)，sa=128/255
            Assert.Equal(new byte[] { 128, 0, 127, 255 }, Slice(canvas, 0));
        }

        [Fact]
        public void 透明画布上叠加半透明图层只降alpha()
        {
            var canvas = new Bitmap(1, 1);
            var source = new Bitmap(1, 1);
            source.SetPixel(0, 0, 200, 100, 50, 255);

            canvas.DrawOver(source, 0, 0, 0.5d);

            Assert.Equal(128, canvas.AlphaAt(0, 0));
            Assert.Equal(new byte[] { 200, 100, 50 }, new[] { canvas.Pixels[0], canvas.Pixels[1], canvas.Pixels[2] });
        }

        [Fact]
        public void 负偏移时只画落在画布内的部分()
        {
            var canvas = new Bitmap(2, 2);
            var source = new Bitmap(2, 2);
            source.SetPixel(0, 0, 0, 0, 0, 0);
            source.SetPixel(1, 0, 11, 22, 33, 255);
            source.SetPixel(0, 1, 0, 0, 0, 0);
            source.SetPixel(1, 1, 44, 55, 66, 255);

            canvas.DrawOver(source, -1, -1);

            Assert.Equal(new byte[] { 44, 55, 66, 255 }, Slice(canvas, 0));
            Assert.True(canvas.AlphaAt(1, 1) == 0);
        }

        [Fact]
        public void 不透明度为零时什么都不画()
        {
            var canvas = new Bitmap(1, 1);
            var source = new Bitmap(1, 1);
            source.SetPixel(0, 0, 255, 255, 255, 255);

            canvas.DrawOver(source, 0, 0, 0d);

            Assert.True(canvas.IsFullyTransparent());
        }

        private static byte[] Slice(Bitmap bitmap, int pixelIndex)
        {
            int start = pixelIndex * 4;
            return new[]
            {
                bitmap.Pixels[start], bitmap.Pixels[start + 1], bitmap.Pixels[start + 2], bitmap.Pixels[start + 3]
            };
        }
    }

    /// <summary>通道数据 → 图层 RGBA 位图，以及图层树合成。</summary>
    public class LayerRasterizerTests
    {
        private const string MarkerName = "</Layer group>";

        [Fact]
        public void Raw通道还原出RGBA像素()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            LayerSpec layer = Square(builder.AddLayer("pixels"));
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 10, 20, 30, 40 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 11, 21, 31, 41 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 12, 22, 32, 42 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 128, 0, 64 });

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(2, bitmap.Width);
            Assert.Equal(2, bitmap.Height);
            Assert.Equal(
                new byte[]
                {
                    10, 11, 12, 255,
                    20, 21, 22, 128,
                    30, 31, 32, 0,
                    40, 41, 42, 64
                },
                bitmap.Pixels);
        }

        [Fact]
        public void RLE通道还原出相同像素()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            LayerSpec layer = Square(builder.AddLayer("pixels"));
            layer.WithChannel(0, PsdCompression.Rle, RleRows(new byte[] { 10, 20 }, new byte[] { 30, 40 }))
                .WithChannel(1, PsdCompression.Rle, RleRows(new byte[] { 11, 21 }, new byte[] { 31, 41 }))
                .WithChannel(2, PsdCompression.Rle, RleRows(new byte[] { 12, 22 }, new byte[] { 32, 42 }))
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Rle,
                    RleRows(new byte[] { 255, 128 }, new byte[] { 0, 64 }));

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(
                new byte[]
                {
                    10, 11, 12, 255,
                    20, 21, 22, 128,
                    30, 31, 32, 0,
                    40, 41, 42, 64
                },
                bitmap.Pixels);
        }

        [Fact]
        public void 缺少透明通道时整层不透明()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            LayerSpec layer = Square(builder.AddLayer("opaque"));
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 5, 6, 7, 8 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 9, 10, 11, 12 });

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(255, bitmap.AlphaAt(0, 0));
            Assert.Equal(255, bitmap.AlphaAt(1, 1));
            Assert.Equal(1d, bitmap.AlphaCoverage());
        }

        [Fact]
        public void 缺失的颜色通道按黑色补零()
        {
            var builder = new PsdFixtureBuilder { Width = 1, Height = 1 };
            LayerSpec layer = builder.AddLayer("onlyRed");
            layer.Right = 1;
            layer.Bottom = 1;
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 200 });

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(new byte[] { 200, 0, 0, 255 }, bitmap.Pixels);
        }

        [Fact]
        public void 十六位样本归一化到八位()
        {
            var builder = new PsdFixtureBuilder { Width = 4, Height = 1, Depth = 16 };
            LayerSpec layer = builder.AddLayer("sixteen");
            layer.Right = 4;
            layer.Bottom = 1;
            layer.WithChannel(0, PsdCompression.Raw, Sample16(0xFFFF, 0x8000, 0x4000, 0x0000));

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(2, file.BytesPerSample);
            Assert.Equal(new byte[] { 255, 128, 64, 0 }, new[]
            {
                bitmap.Pixels[0], bitmap.Pixels[4], bitmap.Pixels[8], bitmap.Pixels[12]
            });
            Assert.Equal(255, bitmap.AlphaAt(0, 0));
        }

        [Fact]
        public void 三十二位浮点样本归一化到八位()
        {
            var builder = new PsdFixtureBuilder { Width = 3, Height = 1, Depth = 32 };
            LayerSpec layer = builder.AddLayer("floats");
            layer.Right = 3;
            layer.Bottom = 1;
            layer.WithChannel(0, PsdCompression.Raw,
                Float32(1f, 0.5f, 0f));

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(new byte[] { 255, 128, 0 }, new[] { bitmap.Pixels[0], bitmap.Pixels[4], bitmap.Pixels[8] });
        }

        [Fact]
        public void 纯色填充层用SoCo颜色铺满()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 1 };
            LayerSpec layer = builder.AddLayer("BgColor");
            layer.Right = 2;
            layer.Bottom = 1;
            layer.WithTag("SoCo", SolidColor(1d, 0.5d, 0d));


            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(new byte[] { 255, 128, 0, 255 }, new[]
            {
                bitmap.Pixels[0], bitmap.Pixels[1], bitmap.Pixels[2], bitmap.Pixels[3]
            });
        }

        [Fact]
        public void 没有像素也没有纯色的图层直接跳过()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            Square(builder.AddLayer("empty"));

            PsdFile file = PsdParser.Read(builder.Build());
            var warnings = new List<string>();

            Assert.Null(LayerRasterizer.Rasterize(file, file.Layers[0], warnings));
            Assert.Contains(warnings, item => item.Contains("没有像素通道"));
        }

        [Fact]
        public void 用户蒙版通道乘到图层alpha上()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 1 };
            LayerSpec layer = builder.AddLayer("masked");
            layer.Right = 2;
            layer.Bottom = 1;
            layer.MaskDataLength = 20;
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 200, 200 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 200 })
                .WithChannel(PsdChannelId.UserMask, PsdCompression.Raw, new byte[] { 128, 0 });

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap bitmap = LayerRasterizer.Rasterize(file, file.Layers[0]);

            Assert.Equal(128, bitmap.AlphaAt(0, 0));
            Assert.Equal(0, bitmap.AlphaAt(1, 0));
        }

        [Fact]
        public void 分组不参与栅格化()
        {
            var builder = new PsdFixtureBuilder { Width = 4, Height = 4 };
            LayerSpec group = builder.AddLayer(MarkerName);
            group.Right = 0;
            group.Bottom = 0;
            group.WithTag("lsct", Divider(3)).WithTag("lsct", Divider(1));

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.True(file.Layers[0].IsGroup);
            Assert.Null(LayerRasterizer.Rasterize(file, file.Layers[0]));
        }

        [Fact]
        public void 隐藏图层默认不出现在绘制调用里()
        {
            var builder = new PsdFixtureBuilder { Width = 4, Height = 4 };
            Filled(builder.AddLayer("hidden"), 0, 0, 255, 0, 0).Flags = 8 | PsdLayerFlags.Hidden;
            Filled(builder.AddLayer("shown"), 0, 0, 0, 255, 0);

            PsdFile file = PsdParser.Read(builder.Build());
            var calls = new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, null));

            Assert.Single(calls);
            Assert.Equal("shown", calls[0].Layer.Name);
        }

        [Fact]
        public void 打开开关后隐藏图层也会被绘制()
        {
            var builder = new PsdFixtureBuilder { Width = 4, Height = 4 };
            Filled(builder.AddLayer("hidden"), 0, 0, 255, 0, 0).Flags = 8 | PsdLayerFlags.Hidden;

            PsdFile file = PsdParser.Read(builder.Build());
            var options = new RasterizeOptions { IncludeHiddenLayers = true };
            var calls = new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, options));

            Assert.Single(calls);
            Assert.Equal("hidden", calls[0].Layer.Name);
        }

        [Fact]
        public void 整层透明的图层不产生绘制调用()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            LayerSpec layer = Square(builder.AddLayer("blank"));
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 });

            PsdFile file = PsdParser.Read(builder.Build());
            var calls = new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, null));

            Assert.Empty(calls);
        }

        [Fact]
        public void 覆盖率低于阈值时视为空图层()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            LayerSpec layer = Square(builder.AddLayer("tiny"));
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 0, 0, 0 });

            PsdFile file = PsdParser.Read(builder.Build());

            // 覆盖率 0.25，默认阈值 0 时仍然要画
            Assert.Single(new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, null)));

            // 阈值提到 0.5 之后这个图层被视为空
            var options = new RasterizeOptions { EmptyLayerCoverage = 0.5d };
            Assert.Empty(new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, options)));

        }

        [Fact]
        public void 绘制调用带着图层不透明度()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            LayerSpec layer = Filled(builder.AddLayer("half"), 0, 0, 255, 0, 0);
            layer.Opacity = 128;

            PsdFile file = PsdParser.Read(builder.Build());
            var calls = new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, null));

            Assert.Single(calls);
            Assert.Equal(128d / 255d, calls[0].Opacity, 6);
        }

        [Fact]
        public void 裁剪层按基准层的alpha裁剪()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };

            // 记录顺序自下而上：基准层先出现，裁剪层紧随其后
            LayerSpec baseLayer = Square(builder.AddLayer("base"));
            baseLayer.WithChannel(0, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 0, 255, 0 });

            LayerSpec clipped = Square(builder.AddLayer("clipped"));
            clipped.Clipping = true;
            clipped.WithChannel(0, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 255, 255, 255 });

            PsdFile file = PsdParser.Read(builder.Build());
            var warnings = new List<string>();
            var calls = new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, null, warnings));

            Assert.Equal(2, calls.Count);
            Bitmap clippedBitmap = calls[1].Bitmap;
            Assert.Equal(255, clippedBitmap.AlphaAt(0, 0));
            Assert.Equal(0, clippedBitmap.AlphaAt(1, 0));
            Assert.Equal(255, clippedBitmap.AlphaAt(0, 1));
            Assert.Equal(0, clippedBitmap.AlphaAt(1, 1));
        }

        [Fact]
        public void 裁剪层的基准层自己也保持原样()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 1 };
            LayerSpec baseLayer = builder.AddLayer("base");
            baseLayer.Right = 2;
            baseLayer.Bottom = 1;
            baseLayer.WithChannel(0, PsdCompression.Raw, new byte[] { 10, 10 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 10, 10 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 10, 10 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 0 });

            LayerSpec clipped = builder.AddLayer("clipped");
            clipped.Right = 2;
            clipped.Bottom = 1;
            clipped.Clipping = true;
            clipped.WithChannel(0, PsdCompression.Raw, new byte[] { 20, 20 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 20, 20 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 20, 20 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 255 });

            PsdFile file = PsdParser.Read(builder.Build());
            var calls = new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, null));

            Assert.Equal(255, calls[0].Bitmap.AlphaAt(0, 0));
            Assert.Equal(0, calls[0].Bitmap.AlphaAt(1, 0));
        }

        [Fact]
        public void 分组会打断裁剪链()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 1 };
            LayerSpec baseLayer = builder.AddLayer("base");
            baseLayer.Right = 2;
            baseLayer.Bottom = 1;
            baseLayer.WithChannel(0, PsdCompression.Raw, new byte[] { 0, 0 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 0, 0 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 0, 0 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 0 });

            LayerSpec marker = builder.AddLayer(MarkerName);
            marker.Right = 0;
            marker.Bottom = 0;
            marker.WithTag("lsct", Divider(3)).WithTag("lsct", Divider(1));

            LayerSpec clipped = builder.AddLayer("clipped");
            clipped.Right = 2;
            clipped.Bottom = 1;
            clipped.Clipping = true;
            clipped.WithChannel(0, PsdCompression.Raw, new byte[] { 0, 0 })
                .WithChannel(1, PsdCompression.Raw, new byte[] { 0, 0 })
                .WithChannel(2, PsdCompression.Raw, new byte[] { 0, 0 })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 255 });

            PsdFile file = PsdParser.Read(builder.Build());
            var calls = new List<LayerRasterizer.LayerDraw>(LayerRasterizer.EnumerateDrawCalls(file, null));

            Assert.Equal(2, calls.Count);
            // 基准层被分组挡在后面，裁剪层拿不到基准，保持原样
            Assert.Equal(255, calls[1].Bitmap.AlphaAt(1, 0));
        }

        [Fact]
        public void 合成结果与画布尺寸一致()
        {
            var builder = new PsdFixtureBuilder { Width = 6, Height = 5 };
            Filled(builder.AddLayer("bg"), 1, 1, 255, 0, 0);

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap canvas = LayerRasterizer.Composite(file);

            Assert.Equal(6, canvas.Width);
            Assert.Equal(5, canvas.Height);
            Assert.Equal(255, canvas.AlphaAt(1, 1));
            Assert.Equal(0, canvas.AlphaAt(0, 0));
            Assert.Equal(0, canvas.AlphaAt(5, 4));
        }

        [Fact]
        public void 合成按记录顺序自下而上叠加()
        {
            var builder = new PsdFixtureBuilder { Width = 4, Height = 4 };
            Filled(builder.AddLayer("bottom"), 0, 0, 255, 0, 0);
            Filled(builder.AddLayer("top"), 0, 0, 0, 0, 255);

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap canvas = LayerRasterizer.Composite(file);

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, new[]
            {
                canvas.Pixels[0], canvas.Pixels[1], canvas.Pixels[2], canvas.Pixels[3]
            });
        }

        [Fact]
        public void 合成会应用图层不透明度()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            LayerSpec layer = Filled(builder.AddLayer("half"), 0, 0, 255, 0, 0);
            layer.Opacity = 128;

            PsdFile file = PsdParser.Read(builder.Build());
            Bitmap canvas = LayerRasterizer.Composite(file);

            Assert.Equal(128, canvas.AlphaAt(0, 0));
            Assert.Equal(new byte[] { 255, 0, 0 }, new[] { canvas.Pixels[0], canvas.Pixels[1], canvas.Pixels[2] });
        }

        [Fact]
        public void 合成跳过隐藏图层()
        {
            var builder = new PsdFixtureBuilder { Width = 2, Height = 2 };
            Filled(builder.AddLayer("hidden"), 0, 0, 255, 0, 0).Flags = 8 | PsdLayerFlags.Hidden;

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.True(LayerRasterizer.Composite(file).IsFullyTransparent());
        }

        [Fact]
        public void 画布尺寸异常时合成返回空()
        {
            // 解析器会直接拒绝 0x0 的画布，这里手工造一个空文件对象走合成入口
            var file = new PsdFile();
            file.Width = 0;
            file.Height = 0;

            Assert.Null(LayerRasterizer.Composite(file));
            Assert.Null(LayerRasterizer.Composite(null));
        }


        private static LayerSpec Square(LayerSpec layer)
        {
            layer.Right = 2;
            layer.Bottom = 2;
            return layer;
        }

        /// <summary>造一个铺满 layer 矩形的不透明单色层。</summary>
        private static LayerSpec Filled(LayerSpec layer, int left, int top, byte r, byte g, byte b)
        {
            layer.Left = left;
            layer.Top = top;
            layer.Right = left + 2;
            layer.Bottom = top + 2;
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { r, r, r, r })
                .WithChannel(1, PsdCompression.Raw, new byte[] { g, g, g, g })
                .WithChannel(2, PsdCompression.Raw, new byte[] { b, b, b, b })
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[] { 255, 255, 255, 255 });
            return layer;
        }

        private static byte[] RleRows(params byte[][] rows)
        {
            var payload = new List<byte>();
            for (int i = 0; i < rows.Length; i++)
            {
                // 每行用一个字面量包，长度 = 1 字节头 + 原始字节
                PsdFixtureBuilder.WriteU16(payload, (ushort)(rows[i].Length + 1));
            }

            for (int i = 0; i < rows.Length; i++)
            {
                payload.Add((byte)(rows[i].Length - 1));
                payload.AddRange(rows[i]);
            }

            return payload.ToArray();
        }

        private static byte[] Sample16(params int[] values)
        {
            var payload = new List<byte>();
            for (int i = 0; i < values.Length; i++)
            {
                PsdFixtureBuilder.WriteU16(payload, (ushort)values[i]);
            }

            return payload.ToArray();
        }

        private static byte[] Float32(params float[] values)
        {
            var payload = new List<byte>();
            for (int i = 0; i < values.Length; i++)
            {
                byte[] raw = System.BitConverter.GetBytes(values[i]);
                payload.Add(raw[3]);
                payload.Add(raw[2]);
                payload.Add(raw[1]);
                payload.Add(raw[0]);
            }

            return payload.ToArray();
        }

        private static byte[] SolidColor(double r, double g, double b)
        {
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, 16);
            payload.AddRange(Desc.ObjectBody("null",
                Desc.Entry("Clr ", Desc.Object("RGBC",
                    Desc.Entry("Rd  ", Desc.Double(r)),
                    Desc.Entry("Grn ", Desc.Double(g)),
                    Desc.Entry("Bl  ", Desc.Double(b))))));
            return payload.ToArray();
        }

        private static byte[] Divider(int value)
        {
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, value);
            return payload.ToArray();
        }
    }
}
