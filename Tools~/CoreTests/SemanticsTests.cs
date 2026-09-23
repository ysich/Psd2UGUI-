using System.Collections.Generic;
using System.Text;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    /// <summary>图层名标签表。</summary>
    public class LayerTagTests
    {
        [Theory]
        [InlineData("ButtonBlack", "ButtonBlack", UiElementType.None, UiRole.None)]
        [InlineData("Ok.bt", "Ok", UiElementType.Button, UiRole.None)]
        [InlineData("Ok.btn", "Ok", UiElementType.Button, UiRole.None)]
        [InlineData("Title.txt", "Title", UiElementType.Text, UiRole.None)]
        [InlineData("Title.tmptxt", "Title", UiElementType.TmpText, UiRole.None)]
        [InlineData("Logo.img", "Logo", UiElementType.Image, UiRole.None)]
        [InlineData("Shot.rimg", "Shot", UiElementType.RawImage, UiRole.None)]
        [InlineData("Body.sv", "Body", UiElementType.ScrollView, UiRole.None)]
        [InlineData("Row.sld", "Row", UiElementType.Slider, UiRole.None)]
        [InlineData("Check.tg", "Check", UiElementType.Toggle, UiRole.None)]
        [InlineData("Edit.ipt", "Edit", UiElementType.InputField, UiRole.None)]
        [InlineData("Kind.dpd", "Kind", UiElementType.Dropdown, UiRole.None)]
        [InlineData("Frame.panel", "Frame", UiElementType.Panel, UiRole.None)]
        [InlineData("Cut.msk", "Cut", UiElementType.Mask, UiRole.None)]
        [InlineData("Tint.col", "Tint", UiElementType.FillColor, UiRole.None)]
        [InlineData("Arrow.img.dpdicon", "Arrow", UiElementType.Image, UiRole.Arrow)]
        [InlineData("Mark.img.mark", "Mark", UiElementType.Image, UiRole.Mark)]
        [InlineData("VbarFill.vbar", "VbarFill", UiElementType.None, UiRole.VerticalScrollbar)]
        [InlineData("VbarBG.vbarbg", "VbarBG", UiElementType.None, UiRole.VerticalScrollbarBackground)]
        [InlineData("SliderBg.bg", "SliderBg", UiElementType.None, UiRole.Background)]
        [InlineData("SliderFill.fill", "SliderFill", UiElementType.None, UiRole.Fill)]
        [InlineData("Knob.handle", "Knob", UiElementType.None, UiRole.Handle)]
        [InlineData("Tips.ph", "Tips", UiElementType.None, UiRole.Placeholder)]
        [InlineData("Tips.placeholder", "Tips", UiElementType.None, UiRole.Placeholder)]
        [InlineData("Hover.onover", "Hover", UiElementType.None, UiRole.Highlight)]
        [InlineData("Down.press", "Down", UiElementType.None, UiRole.Pressed)]
        [InlineData("On.select", "On", UiElementType.None, UiRole.Selected)]
        [InlineData("Gray.disable", "Gray", UiElementType.None, UiRole.Disabled)]
        [InlineData("Label.bttxt", "Label", UiElementType.None, UiRole.ButtonText)]
        [InlineData("Value.ipttxt", "Value", UiElementType.None, UiRole.InputText)]
        [InlineData("Row.template", "Row", UiElementType.None, UiRole.Template)]
        [InlineData("ToggleBg.tg.bg", "ToggleBg", UiElementType.Toggle, UiRole.Background)]
        [InlineData("Row.sld.handle", "Row", UiElementType.Slider, UiRole.Handle)]
        [InlineData("InputBg.ipt.bg", "InputBg", UiElementType.InputField, UiRole.Background)]
        public void 标签解析出类型与角色(string raw, string name, UiElementType type, UiRole role)
        {
            LayerTag tag = LayerTagParser.Parse(raw);

            Assert.Equal(name, tag.Name);
            Assert.Equal(type, tag.Type);
            Assert.Equal(role, tag.Role);
        }

        [Theory]
        [InlineData("Frame.sliced")]
        [InlineData("Frame.slice")]
        [InlineData("Frame.9s")]
        [InlineData("Frame.9")]
        public void 九宫标签被识别(string raw)
        {
            LayerTag tag = LayerTagParser.Parse(raw);

            Assert.True(tag.NineSlice);
            Assert.Equal("Frame", tag.Name);
        }

        [Fact]
        public void 引用标签解析出复用目标()
        {
            LayerTag image = LayerTagParser.Parse("ref ButtonBule");
            Assert.True(image.IsReference);
            Assert.False(image.IsPrefabReference);
            Assert.Equal("ButtonBule", image.ReferenceTarget);
            Assert.Equal("ButtonBule", image.Name);

            LayerTag prefab = LayerTagParser.Parse("refp Dialog.bt");
            Assert.True(prefab.IsReference);
            Assert.True(prefab.IsPrefabReference);
            Assert.Equal("Dialog", prefab.ReferenceTarget);
            Assert.Equal(UiElementType.Button, prefab.Type);
        }

        [Fact]
        public void 忽略标签标记整棵子树()
        {
            LayerTag tag = LayerTagParser.Parse("Sky.ignore");

            Assert.True(tag.Ignored);
            Assert.Equal("Sky", tag.Name);
        }

        [Fact]
        public void 不认识的标签被记录下来()
        {
            LayerTag tag = LayerTagParser.Parse("CircleFilled.filled.bg");

            Assert.Equal("CircleFilled", tag.Name);
            Assert.Equal(UiRole.Background, tag.Role);
            Assert.Equal(new List<string> { "filled" }, tag.UnknownTokens);
        }

        [Fact]
        public void 没有标签的图层名原样保留()
        {
            LayerTag tag = LayerTagParser.Parse("100%");

            Assert.Equal("100%", tag.Name);
            Assert.Empty(tag.Tokens);
            Assert.Equal(UiElementType.None, tag.Type);
        }

        [Fact]
        public void 空名称不会炸()
        {
            LayerTag tag = LayerTagParser.Parse(null);

            Assert.Equal(string.Empty, tag.Name);
            Assert.Empty(tag.Tokens);
        }
    }

    /// <summary>兜底推断规则。</summary>
    public class HeuristicTypeInferrerTests
    {
        private static InferenceContext Context(string layerName, bool text = false, bool group = false,
            bool pixels = true, bool solid = false, bool reference = false)
        {
            LayerTag tag = LayerTagParser.Parse(layerName);
            return new InferenceContext
            {
                Layer = new PsdLayer { Name = layerName, DividerType = group ? PsdSectionDivider.OpenFolder : -1 },
                Tag = tag,
                TaggedType = tag.Type,
                TaggedRole = tag.Role,
                IsGroup = group,
                HasText = text,
                HasPixelData = pixels,
                HasSolidFill = solid,
                IsReference = reference
            };
        }

        [Fact]
        public void 文本层优先判成文本控件()
        {
            var inferrer = new HeuristicTypeInferrer();

            Assert.Equal(UiElementType.TmpText, inferrer.Infer(Context("Title", text: true)));
            Assert.Equal(UiElementType.Text,
                new HeuristicTypeInferrer(UiElementType.Text).Infer(Context("Title", text: true)));
        }

        [Fact]
        public void 分组按选项判成Group或Panel()
        {
            Assert.Equal(UiElementType.Group, new HeuristicTypeInferrer().Infer(Context("Header", group: true)));
            Assert.Equal(UiElementType.Panel,
                new HeuristicTypeInferrer(UiElementType.TmpText, UiElementType.Panel).Infer(Context("Header", group: true)));
        }

        [Fact]
        public void 纯色填充层判成纯色()
        {
            Assert.Equal(UiElementType.FillColor, new HeuristicTypeInferrer().Infer(Context("Tint", pixels: false, solid: true)));
        }

        [Fact]
        public void 名字关键词判出控件类型()
        {
            var inferrer = new HeuristicTypeInferrer();

            Assert.Equal(UiElementType.TmpButton, inferrer.Infer(Context("ButtonBlack")));
            Assert.Equal(UiElementType.TmpToggle, inferrer.Infer(Context("ToggleBg")));
            Assert.Equal(UiElementType.Slider, inferrer.Infer(Context("SliderBg")));
            Assert.Equal(UiElementType.ScrollView, inferrer.Infer(Context("Scroll View")));
            Assert.Equal(UiElementType.Image, inferrer.Infer(Context("Icon_Close")));
            Assert.Equal(UiElementType.Image, inferrer.Infer(Context("DialogBackground")));

            // 换成 UGUI Text 方案时，复合控件也跟着降级
            var ugui = new HeuristicTypeInferrer(UiElementType.Text);
            Assert.Equal(UiElementType.Button, ugui.Infer(Context("ButtonBlack")));
        }

        [Fact]
        public void 写了角色标签就不按名字抢控件()
        {
            // `list.bg` 是列表的背景图，不是列表控件本身
            LayerTag tag = LayerTagParser.Parse("list.bg");
            var context = new InferenceContext
            {
                Layer = new PsdLayer { Name = "list.bg" },
                Tag = tag,
                TaggedRole = tag.Role,
                HasPixelData = true
            };

            Assert.Equal(UiElementType.Image, new HeuristicTypeInferrer().Infer(context));
        }

        [Fact]
        public void 引用节点默认是图片()
        {
            Assert.Equal(UiElementType.Image, new HeuristicTypeInferrer().Infer(Context("ButtonBlack", reference: true)));
            Assert.Equal(UiElementType.Group, new HeuristicTypeInferrer().Infer(Context("Header", group: true, reference: true)));
        }

        [Fact]
        public void 没有像素也没有关键词时返回None()
        {
            Assert.Equal(UiElementType.None, new HeuristicTypeInferrer().Infer(Context("a1b2", pixels: false)));
        }
    }

    /// <summary>节点树构建与覆盖表。</summary>
    public class NodeBuilderTests
    {
        private const string MarkerName = "</Layer group>";

        [Fact]
        public void 分组嵌套还原成节点树()
        {
            var builder = new PsdFixtureBuilder { Width = 40, Height = 30 };
            Group(builder);
            Pixel(builder, "IconClose.img", 1, 1, 5, 5);
            Pixel(builder, "Title.tmptxt", 6, 1, 20, 8);
            EndGroup(builder, "Panel.panel");
            Pixel(builder, "BgCol", 0, 0, 40, 30);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);

            Assert.Equal(2, document.Root.Children.Count);
            UiNode panel = document.Root.Children[0];
            Assert.Equal("Panel", panel.Name);
            Assert.Equal(UiElementType.Panel, panel.Type);
            Assert.Equal(UiElementType.Group, document.Root.Type);
            Assert.Equal(new UiRect(0d, 0d, 40d, 30d), document.Root.Rect);
            Assert.Equal(2, panel.Children.Count);
            Assert.Equal("IconClose", panel.Children[0].Name);
            Assert.Equal(UiElementType.Image, panel.Children[0].Type);
            Assert.Equal("Panel/IconClose", panel.Children[0].LayerPath);
            Assert.Equal(new UiRect(1d, 1d, 4d, 4d), panel.Children[0].Rect);
        }

        [Fact]
        public void 同一个文件两次构建结果完全一致()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "A.img", 0, 0, 4, 4);
            Pixel(builder, "B.bt", 0, 0, 4, 4);

            string first = ContractJson.ToJsonText(NodeBuilder.Build(PsdParser.Read(builder.Build())));
            string second = ContractJson.ToJsonText(NodeBuilder.Build(PsdParser.Read(builder.Build())));

            Assert.Equal(first, second);
        }

        [Fact]
        public void 节点ID来自图层ID与路径()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "A.img", 0, 0, 4, 4);

            UiNode node = NodeBuilder.Build(PsdParser.Read(builder.Build())).Root.Children[0];

            Assert.Equal(StableId.NodeId("A", node.LayerId), node.Id);
        }

        [Fact]
        public void 隐藏图层默认保留并标记不可见()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "Hidden.img", 0, 0, 4, 4).Flags = 8 | PsdLayerFlags.Hidden;
            Pixel(builder, "Shown.img", 0, 0, 4, 4);

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            Assert.Equal(2, document.Root.Children.Count);
            Assert.False(document.Root.Children[0].Visible);
            Assert.True(document.Root.Children[1].Visible);
            Assert.Equal("1", document.Stats["hidden"]);
        }

        [Fact]
        public void 关闭保留隐藏图层后整棵跳过()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "Hidden.img", 0, 0, 4, 4).Flags = 8 | PsdLayerFlags.Hidden;

            var options = new NodeBuildOptions { KeepHiddenLayers = false };
            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()), options);

            Assert.Empty(document.Root.Children);
        }

        [Fact]
        public void ignore标签跳过整棵子树()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Group(builder);
            Pixel(builder, "Inner.img", 0, 0, 4, 4);
            EndGroup(builder, "Temp.ignore");
            Pixel(builder, "Keep.img", 0, 0, 4, 4);

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            Assert.Single(document.Root.Children);
            Assert.Equal("Keep", document.Root.Children[0].Name);
            Assert.Equal("1", document.Stats["ignored"]);
            Assert.Contains(document.Diagnostics, item => item.Code == "node.ignored");
        }

        [Fact]
        public void 文本层映射成节点文本()
        {
            var builder = new PsdFixtureBuilder { Width = 40, Height = 20 };
            LayerSpec layer = builder.AddLayer("Title.tmptxt");
            layer.Right = 40;
            layer.Bottom = 20;
            layer.WithTag("TySh", TypeToolPayload("Hello")).WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            UiNode node = NodeBuilder.Build(PsdParser.Read(builder.Build())).Root.Children[0];

            Assert.Equal(UiElementType.TmpText, node.Type);
            Assert.NotNull(node.Text);
            Assert.Equal("Hello", node.Text.Content);
            Assert.Equal(42d, node.Text.FontSize);
            Assert.Equal("center", node.Text.Align);
            Assert.Equal("#0080ffff", node.Text.Color.ToHexRgba());
            Assert.Equal("impact", node.Text.FontKey);
            Assert.Equal("Impact", node.Text.FontName);
        }

        [Fact]
        public void 纯色填充层带上颜色与类型()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            LayerSpec layer = builder.AddLayer("Tint.col");
            layer.Right = 8;
            layer.Bottom = 8;
            layer.WithTag("SoCo", SolidColor(0.5d, 0.25d, 0d));

            UiNode node = NodeBuilder.Build(PsdParser.Read(builder.Build())).Root.Children[0];

            Assert.Equal(UiElementType.FillColor, node.Type);
            Assert.True(node.HasFill);
            Assert.Equal("#804000ff", node.Fill.ToHexRgba());
        }

        [Fact]
        public void 九宫与引用标签写进节点()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "Frame.sliced", 0, 0, 4, 4);
            Pixel(builder, "ref ButtonBule", 0, 0, 4, 4);

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            Assert.Equal("true", document.Root.Children[0].Tags["nine-slice"]);
            UiNode reference = document.Root.Children[1];
            Assert.Equal("ButtonBule", reference.Name);
            Assert.Equal("ButtonBule", reference.ReferenceTarget);
            Assert.False(reference.IsPrefabReference);
            Assert.Equal("ButtonBule", reference.Tags["ref"]);
            Assert.Equal(UiElementType.Image, reference.Type);
        }

        [Fact]
        public void 不认识的标签留下诊断与节点标签()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "Circle.filled.bg", 0, 0, 4, 4);

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            Assert.Equal("filled", document.Root.Children[0].Tags["unknown-tags"]);
            Assert.Contains(document.Diagnostics, item => item.Code == "tag.unknown");
        }

        [Fact]
        public void 覆盖表修正类型角色与名字()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "ButtonBlack", 0, 0, 8, 8);

            var options = new NodeBuildOptions
            {
                Overrides = NodeOverrides.Parse(
                    "{ \"nodes\": { \"ButtonBlack\": { \"type\": \"image\", \"role\": \"bg\", \"name\": \"BgImage\" } } }")
            };
            UiNode node = NodeBuilder.Build(PsdParser.Read(builder.Build()), options).Root.Children[0];

            Assert.Equal("BgImage", node.Name);
            Assert.Equal(UiElementType.Image, node.Type);
            Assert.Equal(UiRole.Background, node.Role);
        }

        [Fact]
        public void 覆盖表可以按路径与图层ID匹配()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Group(builder);
            Pixel(builder, "Title.txt", 0, 0, 4, 4);
            EndGroup(builder, "Panel");
            Pixel(builder, "Other.img", 0, 0, 4, 4);

            int layerId = PsdParser.Read(builder.Build()).RootLayers[0].Children[0].LayerId;
            var options = new NodeBuildOptions
            {
                Overrides = NodeOverrides.Parse("{ \"nodes\": { \"Panel/Title\": { \"type\": \"tmptext\" }," +
                                                " \"#" + layerId + "\": { \"role\": \"button-text\" } } }")
            };
            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()), options);

            UiNode title = document.Root.Children[0].Children[0];
            Assert.Equal(UiElementType.TmpText, title.Type);
            Assert.Equal(UiRole.ButtonText, title.Role);
        }

        [Fact]
        public void 覆盖表可以忽略节点与指定资源()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "A.img", 0, 0, 4, 4);
            Pixel(builder, "B.img", 0, 0, 4, 4);

            var options = new NodeBuildOptions
            {
                Overrides = NodeOverrides.Parse(
                    "{ \"nodes\": { \"A\": { \"ignore\": true }, \"B\": { \"resource\": \"Shared/Sprite\" } } }")
            };
            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()), options);

            Assert.Single(document.Root.Children);
            Assert.Equal("B", document.Root.Children[0].Name);
            Assert.Equal("Shared/Sprite", document.Root.Children[0].ReferenceTarget);
        }

        [Fact]
        public void 覆盖表可以改层级()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "Loose.img", 0, 0, 4, 4);
            Group(builder);

            Pixel(builder, "Inner.img", 0, 0, 4, 4);
            EndGroup(builder, "Panel");

            var options = new NodeBuildOptions
            {
                Overrides = NodeOverrides.Parse("{ \"nodes\": { \"Loose\": { \"parent\": \"Panel\" } } }")
            };
            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()), options);

            Assert.Single(document.Root.Children);
            UiNode panel = document.Root.Children[0];
            Assert.Equal(2, panel.Children.Count);
            Assert.Equal("Loose", panel.Children[1].Name);
            // 被移动的节点保留原始 PSD 位置信息，方便排查
            Assert.Equal("Loose", panel.Children[1].LayerPath);
        }

        [Fact]
        public void 覆盖表改层级时拒绝循环()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Group(builder);
            Pixel(builder, "Child.img", 0, 0, 4, 4);
            EndGroup(builder, "Panel");

            var options = new NodeBuildOptions
            {
                Overrides = NodeOverrides.Parse("{ \"nodes\": { \"Panel\": { \"parent\": \"Panel/Child\" } } }")
            };
            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()), options);

            Assert.Contains(document.Diagnostics, item => item.Code == "override.parent-cycle");
        }

        [Fact]
        public void 未匹配的覆盖表条目给出警告()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "A.img", 0, 0, 4, 4);

            var options = new NodeBuildOptions
            {
                Overrides = NodeOverrides.Parse("{ \"nodes\": { \"NotExist\": { \"type\": \"button\" } } }")
            };
            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()), options);

            Assert.Contains(document.Diagnostics, item => item.Code == "override.unmatched");
        }

        [Fact]
        public void 自定义推断器优先于启发式()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "ButtonBlack", 0, 0, 4, 4);

            var options = new NodeBuildOptions();
            options.Inferrers.Add(new ConstantInferrer(UiElementType.RawImage));
            UiNode node = NodeBuilder.Build(PsdParser.Read(builder.Build()), options).Root.Children[0];

            Assert.Equal(UiElementType.RawImage, node.Type);
        }

        [Fact]
        public void 自定义推断器返回None时不接管()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "ButtonBlack", 0, 0, 4, 4);

            var options = new NodeBuildOptions();
            options.Inferrers.Add(new ConstantInferrer(UiElementType.None));
            UiNode node = NodeBuilder.Build(PsdParser.Read(builder.Build()), options).Root.Children[0];

            Assert.Equal(UiElementType.TmpButton, node.Type);
        }

        [Fact]
        public void 标签优先于启发式()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "ButtonBlack.img", 0, 0, 4, 4);

            UiNode node = NodeBuilder.Build(PsdParser.Read(builder.Build())).Root.Children[0];

            Assert.Equal(UiElementType.Image, node.Type);
            Assert.Equal("ButtonBlack", node.Name);
        }

        [Fact]
        public void 分组里的文字按父级结构补角色()
        {
            var builder = new PsdFixtureBuilder { Width = 40, Height = 20 };
            Group(builder);
            LayerSpec text = builder.AddLayer("Ok");
            text.Right = 40;
            text.Bottom = 20;
            text.WithTag("TySh", TypeToolPayload("OK")).WithChannel(0, PsdCompression.Raw, new byte[] { 1 });
            EndGroup(builder, "Submit.bt");

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            UiNode button = document.Root.Children[0];
            Assert.Equal(UiElementType.Button, button.Type);
            Assert.Equal(UiRole.ButtonText, button.Children[0].Role);
        }

        [Fact]
        public void 关闭推断后只认标签与覆盖表()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "ButtonBlack", 0, 0, 4, 4);
            Pixel(builder, "Icon.img", 0, 0, 4, 4);

            var options = new NodeBuildOptions { InferTypes = false, InferRoles = false };
            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()), options);

            Assert.Equal(UiElementType.None, document.Root.Children[0].Type);
            Assert.Equal(UiElementType.Image, document.Root.Children[1].Type);
            Assert.Equal("0", document.Stats["inferred"]);
        }

        [Fact]
        public void 统计信息记录节点数与类型分布()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "A.img", 0, 0, 4, 4);
            Pixel(builder, "B.bt", 0, 0, 4, 4);

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            Assert.Equal("2", document.Stats["nodes"]);
            Assert.Equal("button=1, image=1", document.Stats["types"]);
            Assert.Equal("2", document.Stats["tagged"]);
        }

        [Fact]
        public void 契约文本能读回来()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Pixel(builder, "A.img", 0, 0, 4, 4);

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));
            UiDocument restored = ContractJson.Parse(ContractJson.ToJsonText(document));

            Assert.Equal(document.Root.Children.Count, restored.Root.Children.Count);
            Assert.Equal(document.Root.Children[0].Id, restored.Root.Children[0].Id);
            Assert.Equal(document.Root.Children[0].LayerPath, restored.Root.Children[0].LayerPath);
            Assert.Equal(UiElementType.Image, restored.Root.Children[0].Type);
        }

        [Fact]
        public void 解析警告会带进契约诊断()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8, DeclaredLayerCount = 9 };
            Pixel(builder, "A.img", 0, 0, 4, 4);

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            Assert.Contains(document.Diagnostics, item => item.Code == "psd.warning");
        }

        private sealed class ConstantInferrer : ITypeInferrer
        {
            private readonly UiElementType _type;

            public ConstantInferrer(UiElementType type)
            {
                _type = type;
            }

            public string Name
            {
                get { return "constant"; }
            }

            public UiElementType Infer(InferenceContext context)
            {
                return _type;
            }
        }

        private static LayerSpec Pixel(PsdFixtureBuilder builder, string name, int left, int top, int right, int bottom)
        {
            LayerSpec layer = builder.AddLayer(name);
            layer.Left = left;
            layer.Top = top;
            layer.Right = right;
            layer.Bottom = bottom;
            int count = (right - left) * (bottom - top);
            layer.WithChannel(0, PsdCompression.Raw, new byte[count])
                .WithChannel(1, PsdCompression.Raw, new byte[count])
                .WithChannel(2, PsdCompression.Raw, new byte[count])
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, Filled(count, 255));
            return layer;
        }

        /// <summary>开一个分组：先写 lsct=3 的收尾标记，再由 EndGroup 写分组记录。</summary>
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

        private static byte[] Filled(int count, byte value)
        {
            var data = new byte[count];
            for (int i = 0; i < count; i++)
            {
                data[i] = value;
            }

            return data;
        }

        private static byte[] Divider(int value)
        {
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, value);
            return payload.ToArray();
        }

        private static byte[] TypeToolPayload(string text)
        {
            const string engineData =
                "<< /EngineDict << " +
                "/Editor << /Text (Hello) >> " +
                "/StyleRun << /RunArray [ << /StyleSheet << /StyleSheetData << " +
                "/Font 1 /FontSize 42 /Tracking -20 /Leading 50 /AutoLeading false " +
                "/FillColor << /Type 1 /Values [ 1 0 0.5 1 ] >> >> >> >> ] >> " +
                "/ParagraphRun << /RunArray [ << /ParagraphSheet << /Properties << /Justification 2 >> >> >> ] >> " +
                ">> " +
                "/ResourceDict << /FontSet [ << /Name (ArialMT) >> << /Name (Impact) >> ] >> >>";

            var payload = new List<byte>();
            PsdFixtureBuilder.WriteU16(payload, 1);
            PsdFixtureBuilder.WriteF64(payload, 1d);
            PsdFixtureBuilder.WriteF64(payload, 0d);
            PsdFixtureBuilder.WriteF64(payload, 0d);
            PsdFixtureBuilder.WriteF64(payload, 1d);
            PsdFixtureBuilder.WriteF64(payload, 10d);
            PsdFixtureBuilder.WriteF64(payload, 20d);
            PsdFixtureBuilder.WriteU16(payload, 50);
            PsdFixtureBuilder.WriteI32(payload, 16);
            payload.AddRange(Desc.ObjectBody("null",
                Desc.Entry("Txt ", Desc.Text(text)),
                Desc.Entry("EngineData", Desc.Raw(Encoding.ASCII.GetBytes(engineData)))));
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
    }
}
