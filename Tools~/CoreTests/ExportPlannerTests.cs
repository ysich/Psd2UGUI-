using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Xunit;
using Psd2Ugui.Testing;

namespace Psd2Ugui.CoreTests
{
    /// <summary>导出计划：栅格化 → 去重 → 绑定资源 ID → 解析引用。</summary>
    public class ExportPlannerTests
    {
        private const string MarkerName = "</Layer group>";

        [Fact]
        public void 每个图形节点拿到一张贴图与资源ID()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "Bg.img", 4, 4, 10, 20, 30);
            Solid(builder, "Icon.img", 4, 4, 40, 50, 60);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Equal(2, plan.Sprites.Count);
            Assert.Equal(2, plan.Resources.Count);
            Assert.Equal("2", document.Stats["sprites"]);
            Assert.Equal("0", document.Stats["spritesSliced"]);

            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                SpriteExport sprite = plan.Sprites[i];
                Assert.NotNull(sprite.Bitmap);
                Assert.Equal(4, sprite.Bitmap.Width);
                Assert.EndsWith(".png", sprite.FileName);
                Assert.Equal(sprite.ResourceId, plan.Resources[i].Id);
                Assert.Equal(plan.Module, plan.Resources[i].Module);
                Assert.Equal(sprite.FileName, plan.Resources[i].FileName);
                Assert.Equal(sprite.Bitmap.Width, plan.Resources[i].Width);
            }

            Assert.Equal(plan.Sprites[0].ResourceId, document.Root.Children[0].ResourceId);
            Assert.Equal(plan.Sprites[1].ResourceId, document.Root.Children[1].ResourceId);
        }

        [Fact]
        public void 内容相同的图层共用一张图()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "Button.img", 4, 4, 10, 20, 30);
            Solid(builder, "ButtonCopy.img", 4, 4, 10, 20, 30);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Single(plan.Sprites);
            SpriteExport sprite = plan.Sprites[0];
            Assert.Equal(2, sprite.NodeIds.Count);
            Assert.True(sprite.Shared);
            Assert.Equal("1", document.Stats["spriteShared"]);
            Assert.Same(sprite.ResourceId, document.Root.Children[1].ResourceId);
        }

        [Fact]
        public void 内容不同的图层各自落一张图()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "A.img", 4, 4, 10, 20, 30);
            Solid(builder, "B.img", 4, 4, 30, 20, 10);

            PsdFile file = PsdParser.Read(builder.Build());
            ExportPlan plan = ExportPlanner.Build(file, NodeBuilder.Build(file));

            Assert.Equal(2, plan.Sprites.Count);
            Assert.False(plan.Sprites[0].Shared);
            Assert.NotEqual(plan.Sprites[0].ContentHash, plan.Sprites[1].ContentHash);
        }

        [Fact]
        public void 文本纯色分组与忽略节点都不导出贴图()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            builder.AddLayer("Title.txt").WithChannel(0, PsdCompression.Raw, new byte[] { 0, 0, 0, 0 });
            builder.AddLayer("Tint.col").WithTag("SoCo", SolidColorPayload());
            Group(builder);
            Solid(builder, "Inner.img", 4, 4, 1, 2, 3);
            EndGroup(builder, "Panel.panel");

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            // 只有分组里的那张图需要贴图，文本 / 纯色 / 分组自身都不产出资源
            SpriteExport sprite = Assert.Single(plan.Sprites);
            Assert.Equal("Inner", sprite.Name);
            Assert.Contains(document.Root.Children, item => item.Type == UiElementType.Text);
            Assert.Contains(document.Root.Children, item => item.Type == UiElementType.FillColor);
            Assert.Contains(document.Root.Children, item => item.Type == UiElementType.Panel);
        }

        [Theory]
        [InlineData(UiElementType.Text)]
        [InlineData(UiElementType.TmpText)]
        [InlineData(UiElementType.FillColor)]
        [InlineData(UiElementType.Group)]
        [InlineData(UiElementType.Ignore)]
        public void 不需要贴图的类型(UiElementType type)
        {
            Assert.False(ExportPlanner.NeedsSprite(new UiNode { Type = type }));
        }

        [Theory]
        [InlineData(UiElementType.Image)]
        [InlineData(UiElementType.RawImage)]
        [InlineData(UiElementType.Button)]
        [InlineData(UiElementType.Panel)]
        [InlineData(UiElementType.ScrollView)]
        [InlineData(UiElementType.None)]
        public void 需要贴图的类型(UiElementType type)
        {
            Assert.True(ExportPlanner.NeedsSprite(new UiNode { Type = type }));
        }

        [Fact]
        public void ref按名字复用本文件里的图()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "ButtonBule.img", 4, 4, 10, 20, 30);
            Solid(builder, "ref ButtonBule", 4, 4, 200, 0, 0);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Single(plan.Sprites);
            Assert.Single(plan.LocalReferences);
            Assert.Empty(plan.ExternalReferences);
            Assert.True(plan.Sprites[0].Shared);
            Assert.True(plan.Sprites[0].IsReferenced);
            Assert.Equal(plan.Sprites[0].ResourceId, document.Root.Children[1].ResourceId);
            Assert.Equal("1", document.Stats["referencesLocal"]);
        }

        [Fact]
        public void ref找不到时按模块生成外部资源ID并告警()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "ref SharedButton", 4, 4, 10, 20, 30);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Empty(plan.Sprites);
            Assert.Single(plan.ExternalReferences);
            UiNode node = document.Root.Children[0];
            Assert.Equal(StableId.ResourceId(UiResourceKind.Sprite, plan.Module + "/SharedButton"),
                node.ResourceId);
            Assert.Contains(document.Diagnostics, item => item.Code == "resource.reference-external");
            Assert.Equal("1", document.Stats["referencesExternal"]);
        }

        [Fact]
        public void refp节点只给提示不导出贴图()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "refp Dialog.bt", 4, 4, 10, 20, 30);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Empty(plan.Sprites);
            Assert.Empty(plan.ExternalReferences);
            Assert.Contains(document.Diagnostics, item => item.Code == "resource.prefab-reference");
        }

        [Fact]
        public void 隐藏图层默认导出关闭后跳过()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "Hidden.img", 4, 4, 10, 20, 30).Flags = 8 | PsdLayerFlags.Hidden;
            Solid(builder, "Shown.img", 4, 4, 0, 200, 100);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);

            Assert.Equal(2, ExportPlanner.Build(file, document).Sprites.Count);

            PsdFile again = PsdParser.Read(builder.Build());
            UiDocument hidden = NodeBuilder.Build(again);
            ExportPlan plan = ExportPlanner.Build(again, hidden,
                new ExportOptions { IncludeHiddenLayers = false });

            Assert.Single(plan.Sprites);
            Assert.Equal("Shown", plan.Sprites[0].Name);
        }

        [Fact]
        public void 带九宫标签但检测不出来时告警()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Noise(builder, "Frame.sliced", 6, 6);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Single(plan.Sprites);
            Assert.False(plan.Sprites[0].IsSliceable);
            Assert.Contains(document.Diagnostics, item => item.Code == "nine-slice.failed");
        }

        [Fact]
        public void 可切图拿到九宫边框并被最小化()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Ring(builder, "Frame.img", 6, 6, 2);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            SpriteExport sprite = Assert.Single(plan.Sprites);
            Assert.True(sprite.IsSliceable);
            Assert.Equal(2, sprite.Border.Left);
            Assert.Equal(2, sprite.Border.Bottom);
            Assert.Equal(2, sprite.Border.Right);
            Assert.Equal(2, sprite.Border.Top);
            Assert.Equal(5, sprite.Bitmap.Width);
            Assert.Equal(5, sprite.Bitmap.Height);
            Assert.Equal(new UiRect(0d, 0d, 6d, 6d), sprite.SourceRect);
            Assert.Equal(1, plan.SliceableCount);
            Assert.Equal("1", document.Stats["spritesSliced"]);
            Assert.Equal(sprite.Border, plan.Resources[0].Border);
        }

        [Fact]
        public void 文件名带上内容与九宫()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Ring(builder, "Frame.img", 6, 6, 2);

            PsdFile file = PsdParser.Read(builder.Build());
            ExportPlan plan = ExportPlanner.Build(file, NodeBuilder.Build(file));
            SpriteExport sprite = plan.Sprites[0];

            Assert.Equal(
                StableId.FileName(sprite.Name, sprite.Key, sprite.Bitmap.Width, sprite.Bitmap.Height),
                sprite.FileName);
        }

        [Fact]
        public void 关掉九宫检测时整图落盘()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Ring(builder, "Frame.img", 6, 6, 2);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document,
                new ExportOptions { DetectNineSlice = false });

            SpriteExport sprite = plan.Sprites[0];
            Assert.False(sprite.IsSliceable);
            Assert.Equal(6, sprite.Bitmap.Width);
            Assert.Equal(new UiBorder(0, 0, 0, 0), sprite.Border);
        }

        [Fact]
        public void 没有像素的图层只给提示()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "Ok.img", 4, 4, 1, 2, 3);
            builder.AddLayer("Empty.img");

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Single(plan.Sprites);
            Assert.Contains(document.Diagnostics, item => item.Code == "resource.empty");
        }

        [Fact]
        public void 全透明的图层被跳过()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "Ok.img", 4, 4, 1, 2, 3);
            var transparent = builder.AddLayer("Clear.img");
            transparent.Right = 4;
            transparent.Bottom = 4;
            transparent.WithChannel(0, PsdCompression.Raw, new byte[16])
                .WithChannel(1, PsdCompression.Raw, new byte[16])
                .WithChannel(2, PsdCompression.Raw, new byte[16])
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, new byte[16]);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Single(plan.Sprites);
            Assert.Contains(document.Diagnostics, item => item.Code == "resource.empty");
        }

        [Fact]
        public void 空输入返回空计划()
        {
            ExportPlan plan = ExportPlanner.Build(null, null);

            Assert.Empty(plan.Sprites);
            Assert.Empty(plan.Resources);
            Assert.Empty(plan.LocalReferences);
            Assert.Equal(0L, plan.TotalBytes);
        }

        [Fact]
        public void 模块名默认取文档自己的()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Solid(builder, "A.img", 4, 4, 1, 2, 3);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file, new NodeBuildOptions { Module = "login" });
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Equal("login", plan.Module);
            Assert.Equal("login", plan.Resources[0].Module);
            Assert.Equal("Assets/PSD2UGUI", plan.AssetRoot);
        }

        [Fact]
        public void 选项能覆盖模块名()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Solid(builder, "A.img", 4, 4, 1, 2, 3);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file, new NodeBuildOptions { Module = "login" });
            ExportPlan plan = ExportPlanner.Build(file, document,
                new ExportOptions { Module = "hud", AssetRoot = "Assets/Art/PSD" });

            Assert.Equal("hud", plan.Module);
            Assert.Equal("hud", document.Document.Module);
            Assert.Equal("Assets/Art/PSD", plan.AssetRoot);
        }

        [Fact]
        public void 同一份PSD两次导出结果完全一致()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "A.img", 4, 4, 1, 2, 3);
            Ring(builder, "Frame.img", 6, 6, 2);
            Solid(builder, "ref A", 4, 4, 0, 0, 0);

            string First()
            {
                PsdFile file = PsdParser.Read(builder.Build());
                UiDocument document = NodeBuilder.Build(file);
                ExportPlanner.Build(file, document);
                return ContractJson.ToJsonText(document);
            }

            string first = First();
            string second = First();

            Assert.Equal(first, second);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);
            Assert.Equal(2, plan.Sprites.Count);
            Assert.Single(plan.LocalReferences);
        }

        [Fact]
        public void 统计里能看到总字节数()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "A.img", 4, 4, 1, 2, 3);
            Solid(builder, "B.img", 8, 8, 4, 5, 6);

            PsdFile file = PsdParser.Read(builder.Build());
            ExportPlan plan = ExportPlanner.Build(file, NodeBuilder.Build(file));

            // 4x4 与 8x8 各 RGBA 四字节
            Assert.Equal(4 * 4 * 4 + 8 * 8 * 4, plan.TotalBytes);
        }

        [Fact]
        public void 叶子图层裁到内容框并记下内容框()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Padding(builder, "Icon.img", 6, 6, 2);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            SpriteExport sprite = Assert.Single(plan.Sprites);
            Assert.Equal(2, sprite.Bitmap.Width);
            Assert.Equal(2, sprite.Bitmap.Height);
            Assert.Equal(new UiRect(2d, 2d, 2d, 2d), sprite.SourceRect);
            // 装配侧按这个内容框把节点矩形一起收进去，图才不会被拉满整张图层矩形
            Assert.Equal(new UiRect(2d, 2d, 2d, 2d), document.Root.Children[0].ContentRect.Value);
        }

        [Fact]
        public void 矩形不只给自己用的图层整张导出()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Padding(builder, "Icon.img", 6, 6, 2, 10, 20, 30);
            Padding(builder, "Bar.fill.img", 6, 6, 2, 40, 50, 60);
            Padding(builder, "Knob.handle.img", 6, 6, 2, 70, 80, 90);
            Padding(builder, "List.vpt.img", 6, 6, 2, 100, 110, 120);
            // 状态图得待在按钮组里：直接放在根上的状态图会把整层兄弟都拖下水
            Group(builder);
            Padding(builder, "ButtonBlack.bg.img", 6, 6, 2, 130, 140, 150);
            Padding(builder, "Down.hover.img", 6, 6, 2, 160, 170, 180);
            EndGroup(builder, "BlackBt.btn");

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Equal(6, plan.Sprites.Count);
            // 只有那个叶子图标能收：它的矩形纯属自己用
            Assert.Equal(new UiRect(2d, 2d, 2d, 2d), plan.Sprites[0].SourceRect);
            Assert.Equal(new UiRect(2d, 2d, 2d, 2d), document.Root.Children[0].ContentRect.Value);

            // 填充图会被 uGUI 按 Filled 铺满、滑块的矩形运行时会被改、
            // 视口是滚动内容的参照、按钮组自身是子节点定位的参照：这些都只能整张导
            for (int i = 1; i < document.Root.Children.Count; i++)
            {
                Assert.Null(document.Root.Children[i].ContentRect);
            }

            // 按钮组里的底图与状态图铺在同一个矩形里，谁被裁了都会跟对方错位
            UiNode button = document.Root.Children[4];
            Assert.Equal(2, button.Children.Count);
            for (int i = 0; i < button.Children.Count; i++)
            {
                Assert.Null(button.Children[i].ContentRect);
            }

            for (int i = 1; i < plan.Sprites.Count; i++)
            {
                Assert.Equal(new UiRect(0d, 0d, 6d, 6d), plan.Sprites[i].SourceRect);
                Assert.Equal(6, plan.Sprites[i].Bitmap.Width);
            }
        }

        [Fact]
        public void 被引用的图层整张导出()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Padding(builder, "Icon.img", 6, 6, 2);
            Solid(builder, "ref Icon.img", 4, 4, 1, 2, 3);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            SpriteExport sprite = Assert.Single(plan.Sprites);
            Assert.Single(plan.LocalReferences);
            Assert.Equal(new UiRect(0d, 0d, 6d, 6d), sprite.SourceRect);
            Assert.Equal(6, sprite.Bitmap.Width);
            Assert.Null(document.Root.Children[0].ContentRect);
        }

        [Fact]
        public void 有子节点的图层整张导出()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Padding(builder, "Box.img", 6, 6, 2);
            Padding(builder, "Icon.img", 4, 4, 1);

            PsdFile file = PsdParser.Read(builder.Build());
            var overrides = new NodeOverrides();
            overrides.Entries.Add(new NodeOverride { Key = "Icon", Parent = "Box" });
            UiDocument document = NodeBuilder.Build(file, new NodeBuildOptions { Overrides = overrides });
            ExportPlan plan = ExportPlanner.Build(file, document);

            Assert.Equal(2, plan.Sprites.Count);
            // 盒子的矩形是子节点定位的参照，只能整张导；里面的图照样可以裁
            Assert.Equal(new UiRect(0d, 0d, 6d, 6d), plan.Sprites[0].SourceRect);
            Assert.Null(document.Root.Children[0].ContentRect);
            Assert.Equal(new UiRect(1d, 1d, 2d, 2d), plan.Sprites[1].SourceRect);
            Assert.Equal(new UiRect(1d, 1d, 2d, 2d), document.Root.Children[0].Children[0].ContentRect.Value);
        }

        [Fact]
        public void 可切图补回透明边时边框跟着外扩()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            // 10x10 的图层，四周各留 2 像素透明边，里面是 6x6 的九宫框（边框 2）
            PaddingRing(builder, "Frame.hover.img", 10, 10, 2, 2);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan plan = ExportPlanner.Build(file, document);

            SpriteExport sprite = Assert.Single(plan.Sprites);
            Assert.True(sprite.IsSliceable);
            // 贴图回到整张图层矩形：内容框的 2 像素边框 + 补回来的 2 像素透明边
            Assert.Equal(new UiRect(0d, 0d, 10d, 10d), sprite.SourceRect);
            Assert.Equal(9, sprite.Bitmap.Width);
            Assert.Equal(4, sprite.Border.Left);
            Assert.Equal(4, sprite.Border.Bottom);
            Assert.Equal(4, sprite.Border.Right);
            Assert.Equal(4, sprite.Border.Top);
            Assert.Null(document.Root.Children[0].ContentRect);
        }

        private static byte[] SolidColorPayload()
        {
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, 16);
            payload.AddRange(Desc.ObjectBody("null",
                Desc.Entry("Clr ", Desc.Object("RGBC",
                    Desc.Entry("Rd  ", Desc.Double(0.1d)),
                    Desc.Entry("Grn ", Desc.Double(0.2d)),
                    Desc.Entry("Bl  ", Desc.Double(0.3d))))));
            return payload.ToArray();
        }

        /// <summary>铺满图层矩形的单色不透明图。</summary>
        private static LayerSpec Solid(PsdFixtureBuilder builder, string name, int width, int height,
            byte r, byte g, byte b)
        {
            return Pixels(builder, name, width, height, (x, y) => new[] { r, g, b, (byte)255 });
        }

        /// <summary>
        /// 一张“可切”的图：中间是纯色可拉伸区，四周边框按行列各有自己的颜色。
        ///
        /// 边框颜色刻意做成「同一列/行内一致、相邻行列不同」，这样
        /// 只有中间那两行两列会构成重复区间，九宫检测能唯一地定位到它；
        /// 如果边框像纯色那么均匀，检测器会在边上先找到一段重复行，判出错误的边框。
        /// </summary>
        private static LayerSpec Ring(PsdFixtureBuilder builder, string name, int width, int height, int border)
        {
            return Pixels(builder, name, width, height, (x, y) => RingPixel(x, y, width, height, border));
        }

        /// <summary>九宫框的配色：边框按行列各有颜色，只有中间那两行两列彼此相同。</summary>
        private static byte[] RingPixel(int x, int y, int width, int height, int border)
        {
            bool inner = x >= border && x < width - border && y >= border && y < height - border;
            if (inner)
            {
                return new byte[] { 200, 200, 200, 255 };
            }

            int xTerm = x < border ? x + 1 : (x >= width - border ? x + 5 : 0);
            int yTerm = y < border ? y + 1 : (y >= height - border ? y + 5 : 0);
            return new byte[] { (byte)(30 + xTerm * 20), (byte)(30 + yTerm * 20), 90, 255 };
        }

        /// <summary>四周留一圈全透明边的方块。</summary>
        private static LayerSpec Padding(PsdFixtureBuilder builder, string name, int width, int height, int pad)
        {
            return Padding(builder, name, width, height, pad, 200, 100, 50);
        }

        /// <summary>同上，指定内容颜色：几块图内容一样会被去重并成一张，测矩形时得岔开。</summary>
        private static LayerSpec Padding(PsdFixtureBuilder builder, string name, int width, int height, int pad,
            byte red, byte green, byte blue)
        {
            return Pixels(builder, name, width, height, (x, y) =>
                x >= pad && x < width - pad && y >= pad && y < height - pad
                    ? new byte[] { red, green, blue, 255 }
                    : new byte[] { 0, 0, 0, 0 });
        }

        /// <summary>带一圈透明边的九宫框：去空边与补回透明边要同时成立。</summary>
        private static LayerSpec PaddingRing(PsdFixtureBuilder builder, string name, int width, int height,
            int pad, int border)
        {
            int innerWidth = width - pad * 2;
            int innerHeight = height - pad * 2;
            return Pixels(builder, name, width, height, (x, y) =>
                x < pad || y < pad || x >= width - pad || y >= height - pad
                    ? new byte[] { 0, 0, 0, 0 }
                    : RingPixel(x - pad, y - pad, innerWidth, innerHeight, border));
        }

        /// <summary>每行都不同的噪点图：九宫检测必须判为不可切。</summary>
        private static LayerSpec Noise(PsdFixtureBuilder builder, string name, int width, int height)
        {
            return Pixels(builder, name, width, height,
                (x, y) => new[] { (byte)(x * 37 + 3), (byte)(y * 61 + 7), (byte)(x * y + 11), (byte)255 });
        }

        private static LayerSpec Pixels(PsdFixtureBuilder builder, string name, int width, int height,
            System.Func<int, int, byte[]> pixel)
        {
            LayerSpec layer = builder.AddLayer(name);
            layer.Left = 0;
            layer.Top = 0;
            layer.Right = width;
            layer.Bottom = height;
            int count = width * height;
            var red = new byte[count];
            var green = new byte[count];
            var blue = new byte[count];
            var alpha = new byte[count];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte[] rgba = pixel(x, y);
                    int index = y * width + x;
                    red[index] = rgba[0];
                    green[index] = rgba[1];
                    blue[index] = rgba[2];
                    alpha[index] = rgba[3];
                }
            }

            layer.WithChannel(0, PsdCompression.Raw, red)
                .WithChannel(1, PsdCompression.Raw, green)
                .WithChannel(2, PsdCompression.Raw, blue)
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, alpha);
            return layer;
        }

        private static void Group(PsdFixtureBuilder builder)
        {
            LayerSpec marker = builder.AddLayer(MarkerName);
            marker.Right = 0;
            marker.Bottom = 0;
            marker.WithTag("lsct", Divider(3)).WithChannel(0, PsdCompression.Raw, new byte[] { 0 });
        }

        private static void EndGroup(PsdFixtureBuilder builder, string name)
        {
            LayerSpec group = builder.AddLayer(name);
            group.Right = 0;
            group.Bottom = 0;
            group.WithTag("lsct", Divider(1)).WithChannel(0, PsdCompression.Raw, new byte[] { 0 });
        }

        private static byte[] Divider(int value)
        {
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, value);
            return payload.ToArray();
        }
    }
}
