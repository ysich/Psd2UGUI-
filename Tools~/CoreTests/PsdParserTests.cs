using System.Collections.Generic;
using System.Text;
using Psd2Ugui.Core.Psd;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    public class PsdParserTests
    {
        private const string MarkerName = "</Layer group>";

        private static byte[] UnicodeString(string value)
        {
            var bytes = new List<byte>();
            PsdFixtureBuilder.WriteI32(bytes, value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                PsdFixtureBuilder.WriteU16(bytes, value[i]);
            }

            return bytes.ToArray();
        }

        [Fact]
        public void 解析最小文件得到画布与图层()
        {
            var builder = new PsdFixtureBuilder { Width = 120, Height = 80 };
            builder.AddLayer("Bg").WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 })
                .WithChannel(-1, PsdCompression.Raw, new byte[] { 9, 9, 9, 9 });

            PsdFile file = PsdParser.Read(builder.Build(), "mini.psd");

            Assert.Equal("mini.psd", file.FileName);
            Assert.Equal(120, file.Width);
            Assert.Equal(80, file.Height);
            Assert.Equal(3, file.ColorMode);
            Assert.Equal(8, file.BitDepth);
            Assert.Single(file.Layers);
            Assert.Equal("Bg", file.Layers[0].Name);
            Assert.Single(file.RootLayers);
            Assert.Empty(file.Warnings);
        }

        [Fact]
        public void 文件签名错误时抛出解析异常()
        {
            byte[] data = new PsdFixtureBuilder().Build();
            data[0] = (byte)'X';

            PsdParseException exception = Assert.Throws<PsdParseException>(() => PsdParser.Read(data));
            Assert.Contains("8BPS", exception.Message);
        }

        [Fact]
        public void 文件过小时抛出解析异常()
        {
            Assert.Throws<PsdParseException>(() => PsdParser.Read(new byte[10]));
            Assert.Throws<PsdParseException>(() => PsdParser.Read(null));
        }

        [Fact]
        public void 画布尺寸非法时抛出解析异常()
        {
            byte[] data = new PsdFixtureBuilder().Build();
            // 文件头里高度在偏移 14，宽度在 18
            data[14] = data[15] = data[16] = data[17] = 0;

            Assert.Throws<PsdParseException>(() => PsdParser.Read(data));
        }

        [Fact]
        public void 版本2的文件按PSB解析()
        {
            var builder = new PsdFixtureBuilder { Version = 2 };
            builder.AddLayer("Big").WithChannel(0, PsdCompression.Raw, new byte[] { 7, 7, 7, 7 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.True(file.IsPsb);
            Assert.Single(file.Layers);
            Assert.Equal("Big", file.Layers[0].Name);
        }

        [Fact]
        public void 图层名称优先使用luni的Unicode名称()
        {
            var builder = new PsdFixtureBuilder();
            LayerSpec layer = builder.AddLayer("ascii-name");
            layer.WithTag("luni", UnicodeString("登录按钮"));
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Equal("登录按钮", file.Layers[0].DisplayName);
            Assert.Equal("ascii-name", file.Layers[0].Name);
        }

        [Fact]
        public void 隐藏位为1时图层不可见()
        {
            var visible = new PsdFixtureBuilder();
            visible.AddLayer("show").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            var hidden = new PsdFixtureBuilder();
            LayerSpec lay = hidden.AddLayer("hide");
            lay.Flags = 10; // 0b1010：0x02 置位表示隐藏
            lay.WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            Assert.True(PsdParser.Read(visible.Build()).Layers[0].Visible);
            Assert.False(PsdParser.Read(hidden.Build()).Layers[0].Visible);
        }

        [Fact]
        public void 矩形与不透明度按记录还原()
        {
            var builder = new PsdFixtureBuilder();
            LayerSpec layer = builder.AddLayer("panel");
            layer.Left = 10;
            layer.Top = 20;
            layer.Right = 60;
            layer.Bottom = 45;
            layer.Opacity = 128;
            layer.Clipping = true;
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdLayer parsed = PsdParser.Read(builder.Build()).Layers[0];

            Assert.Equal(10d, parsed.Rect.X);
            Assert.Equal(20d, parsed.Rect.Y);
            Assert.Equal(50d, parsed.Rect.Width);
            Assert.Equal(25d, parsed.Rect.Height);
            Assert.Equal(128, parsed.Opacity);
            Assert.True(parsed.Clipping);
            Assert.Equal(50, parsed.Width);
            Assert.Equal(25, parsed.Height);
        }

        [Fact]
        public void 带蒙版的图层会标记HasMask()
        {
            var builder = new PsdFixtureBuilder();
            LayerSpec masked = builder.AddLayer("masked");
            masked.MaskDataLength = 18; // Photoshop 的蒙版块固定 18 字节
            masked.WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 });
            builder.AddLayer("plain").WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.True(file.Layers[0].HasMask);
            Assert.False(file.Layers[1].HasMask);
            Assert.Empty(file.Warnings);
        }

        [Fact]
        public void lyid解析出稳定图层编号()
        {
            var builder = new PsdFixtureBuilder();
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, 4242);
            builder.AddLayer("x").WithTag("lyid", payload.ToArray())
                .WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            Assert.Equal(4242, PsdParser.Read(builder.Build()).Layers[0].LayerId);
        }

        [Fact]
        public void 图层编号自动带上且唯一()
        {
            var builder = new PsdFixtureBuilder();
            builder.AddLayer("a").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            builder.AddLayer("b").WithChannel(0, PsdCompression.Raw, new byte[] { 2 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Equal(1, file.Layers[0].LayerId);
            Assert.Equal(2, file.Layers[1].LayerId);
            Assert.Empty(file.Warnings);
        }

        [Fact]
        public void 没有lyid的老文件补上唯一编号()
        {
            var builder = new PsdFixtureBuilder { AutoLayerIds = false };
            builder.AddLayer("a").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            builder.AddLayer("b").WithChannel(0, PsdCompression.Raw, new byte[] { 2 });
            builder.AddLayer("c").WithChannel(0, PsdCompression.Raw, new byte[] { 3 });

            PsdFile file = PsdParser.Read(builder.Build());

            // 三个图层都不能是 -1，否则按 ID 查图层会永远只查到第一个
            Assert.Equal(new[] { 0, 1, 2 }, new[] { file.Layers[0].LayerId, file.Layers[1].LayerId, file.Layers[2].LayerId });
            Assert.Same(file.Layers[1], file.FindLayer(file.Layers[1].LayerId));
            Assert.Contains(file.Warnings, item => item.Contains("lyid"));
        }

        [Fact]
        public void 部分图层没有lyid时补的编号不与真实编号撞车()
        {
            var builder = new PsdFixtureBuilder { AutoLayerIds = false };
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, 7);
            builder.AddLayer("a").WithTag("lyid", payload.ToArray())
                .WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            builder.AddLayer("b").WithChannel(0, PsdCompression.Raw, new byte[] { 2 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Equal(7, file.Layers[0].LayerId);
            Assert.Equal(8, file.Layers[1].LayerId);
        }

        [Fact]
        public void 分隔符还原出分组层级()
        {
            var builder = new PsdFixtureBuilder();

            // 真实文件里每个分组前面都有一条 lsct=3 的收尾标记，它只负责开括号
            builder.AddLayer(MarkerName).WithTag("lsct", Divider(3)).WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            builder.AddLayer("childA").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            builder.AddLayer("childB").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            builder.AddLayer("MyGroup").WithTag("lsct", Divider(1)).WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            builder.AddLayer("outside").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            // 记录自下而上排列，所以最后写入的 outside 排在数组末尾
            Assert.Equal(2, file.RootLayers.Count);
            Assert.Equal("MyGroup", file.RootLayers[0].Name);
            Assert.Equal("outside", file.RootLayers[1].Name);
            Assert.True(file.RootLayers[0].IsGroup);
            Assert.Equal(2, file.RootLayers[0].Children.Count);
            Assert.Equal("childA", file.RootLayers[0].Children[0].Name);
            Assert.Equal("childB", file.RootLayers[0].Children[1].Name);
            Assert.Empty(file.Warnings);
        }


        [Fact]
        public void 未闭合的分组把内容上提并记录警告()
        {
            var builder = new PsdFixtureBuilder();
            builder.AddLayer("marker").WithTag("lsct", Divider(3)).WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            builder.AddLayer("orphan").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            // 分组没有对应的收尾记录，里面的图层被上提到根层，同时留下警告
            Assert.Single(file.RootLayers);
            Assert.Equal("orphan", file.RootLayers[0].Name);
            Assert.Contains(file.Warnings, warning => warning.Contains("未闭合的分组"));

        }

        [Fact]
        public void 图像资源解析出分辨率()
        {
            var builder = new PsdFixtureBuilder();
            var resolution = new List<byte>();
            PsdFixtureBuilder.WriteI32(resolution, 300 << 16);
            PsdFixtureBuilder.WriteU16(resolution, 1);
            PsdFixtureBuilder.WriteU16(resolution, 1);
            PsdFixtureBuilder.WriteU16(resolution, 1);
            PsdFixtureBuilder.WriteU16(resolution, 1);
            resolution.AddRange(new byte[8]);
            builder.Resources.Add(new KeyValuePair<int, byte[]>(1005, resolution.ToArray()));
            builder.AddLayer("x").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Single(file.ImageResources);
            Assert.Equal(1005, file.ImageResources[0].Id);
            Assert.Equal(300d, file.ResolutionPpi);
            Assert.Empty(file.Warnings);
        }

        [Fact]
        public void 图像资源签名异常时记录警告并继续()
        {
            var builder = new PsdFixtureBuilder();
            builder.Resources.Add(new KeyValuePair<int, byte[]>(1005, new byte[16]));
            builder.AddLayer("x").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            byte[] data = builder.Build();
            int index = IndexOf(data, Encoding.ASCII.GetBytes("8BIM"));
            data[index] = (byte)'X';

            PsdFile file = PsdParser.Read(data);

            Assert.Contains(file.Warnings, warning => warning.Contains("图像资源段签名异常"));
            Assert.Single(file.Layers);
        }

        [Fact]
        public void 文档级附加信息块按四字节对齐()
        {
            var aligned = new PsdFixtureBuilder { PadDocumentBlocks = true };
            aligned.DocumentBlocks.Add(new KeyValuePair<string, byte[]>("Patt", new byte[5]));
            aligned.DocumentBlocks.Add(new KeyValuePair<string, byte[]>("cinf", new byte[6]));
            aligned.AddLayer("x").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            var unaligned = new PsdFixtureBuilder { PadDocumentBlocks = false };
            unaligned.DocumentBlocks.Add(new KeyValuePair<string, byte[]>("Patt", new byte[5]));
            unaligned.DocumentBlocks.Add(new KeyValuePair<string, byte[]>("cinf", new byte[6]));
            unaligned.AddLayer("x").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            Assert.DoesNotContain(PsdParser.Read(aligned.Build()).Warnings,
                warning => warning.Contains("附加信息"));
            Assert.Contains(PsdParser.Read(unaligned.Build()).Warnings,
                warning => warning.Contains("文档级附加信息签名异常"));
        }

        [Fact]
        public void 通道数据缺失时记录警告且不崩溃()
        {
            var builder = new PsdFixtureBuilder();
            var channel = new ChannelSpec { Id = 0, Compression = PsdCompression.Raw, Payload = new byte[] { 1, 2, 3, 4 } };
            channel.OmitData = true;
            builder.AddLayer("broken").Channels.Add(channel);

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Single(file.Layers);
        }

        [Fact]
        public void 附加数据长度异常时记录警告()
        {
            var builder = new PsdFixtureBuilder();
            LayerSpec layer = builder.AddLayer("weird");
            layer.ExtraLengthDelta = 64;
            layer.WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.NotEmpty(file.Warnings);
        }

        [Fact]
        public void 声明的图层数多于实际时记录警告()
        {
            var builder = new PsdFixtureBuilder { DeclaredLayerCount = 5 };
            builder.AddLayer("only").WithChannel(0, PsdCompression.Raw, new byte[] { 1, 2, 3, 4 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Contains(file.Warnings, warning => warning.Contains("图层记录"));

        }

        [Fact]
        public void 图层数量离谱时放弃图层解析()
        {
            var builder = new PsdFixtureBuilder { DeclaredLayerCount = 30001 };
            builder.AddLayer("only");

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Empty(file.Layers);
            Assert.Contains(file.Warnings, warning => warning.Contains("图层数量异常"));
        }

        [Fact]
        public void 缺少合成图像数据时记录警告()
        {
            var builder = new PsdFixtureBuilder { OmitCompositeHeader = true };
            builder.AddLayer("x").WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Contains(file.Warnings, warning => warning.Contains("缺少合成图像数据"));
        }

        [Fact]
        public void 截断的文件不会抛未捕获异常()
        {
            var builder = new PsdFixtureBuilder();
            builder.AddLayer("x").WithChannel(0, PsdCompression.Rle,
                new byte[] { 0, 2, 0, 2, 1, 5, 6, 1, 7, 8 });
            byte[] full = builder.Build();

            for (int length = 26; length < full.Length; length++)
            {
                var truncated = new byte[length];
                System.Array.Copy(full, truncated, length);
                try
                {
                    PsdFile file = PsdParser.Read(truncated);
                    Assert.NotNull(file);
                }
                catch (PsdParseException)
                {
                    // 结构性损坏允许抛出统一的解析异常，但不能是别的异常类型
                }
            }
        }

        [Fact]
        public void 图层通道可以按需解码出像素()

        {
            var builder = new PsdFixtureBuilder { Width = 4, Height = 4 };
            LayerSpec layer = builder.AddLayer("pixels");
            layer.Right = 2;
            layer.Bottom = 2;
            layer.WithChannel(0, PsdCompression.Rle, PackBitsRows(0x11, 0x22, 0x33, 0x44));

            PsdFile file = PsdParser.Read(builder.Build());
            byte[] pixels = file.ReadChannel(file.Layers[0], file.Layers[0].Channels[0]);

            Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, pixels);
            // 再次读取走缓存，结果一致
            Assert.Same(pixels, file.ReadChannel(file.Layers[0], file.Layers[0].Channels[0]));
        }

        [Fact]
        public void 可按键值找到图层并返回空()
        {
            var builder = new PsdFixtureBuilder();
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, 77);
            builder.AddLayer("hit").WithTag("lyid", payload.ToArray()).WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.NotNull(file.FindLayer(77));
            Assert.Null(file.FindLayer(78));
        }

        private static byte[] Divider(int type)
        {
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, type);
            return payload.ToArray();
        }

        /// <summary>把每个像素单独编码成一行 PackBits 数据，用于 2x2 通道。</summary>
        private static byte[] PackBitsRows(params byte[] values)
        {
            var payload = new List<byte>();
            for (int i = 0; i < values.Length; i += 2)
            {
                PsdFixtureBuilder.WriteU16(payload, 3);
            }


            for (int i = 0; i < values.Length; i += 2)
            {
                payload.Add(1); // PackBits 字面量：后随 2 个原始字节
                payload.Add(values[i]);
                payload.Add(values[i + 1]);
            }


            return payload.ToArray();
        }

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (int i = 0; i + pattern.Length <= data.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
