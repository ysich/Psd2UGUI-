using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Json;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    internal static class SampleDocument
    {
        public static UiDocument Create()
        {
            var document = new UiDocument();
            document.Document = new PsdMeta
            {
                Name = "登录界面",
                FileName = "Login.psd",
                SourcePath = "Assets/UI/Login.psd",
                Module = "login",
                Width = 1920,
                Height = 1080,
                ChannelCount = 3,
                BitDepth = 8,
                ColorMode = 3,
                LayerCount = 4,
                ResolutionPpi = 72d
            };

            document.Resources.Add(new UiResource
            {
                Id = StableId.ResourceId(UiResourceKind.Sprite, "Panel/bg"),
                Kind = UiResourceKind.Sprite,
                Name = "bg",
                Module = "login",
                FileName = "bg_1920x1080_abcd1234.png",
                ContentHash = "abcd1234",
                Width = 1920,
                Height = 1080,
                Border = new UiBorder(12, 12, 12, 12),
                SourceLayerId = 3,
                SourceLayerPath = "Panel/bg",
                SourceRect = new UiRect(0d, 0d, 1920d, 1080d)
            });

            var root = new UiNode
            {
                Id = StableId.NodeId("Panel", 2),
                Name = "Panel",
                LayerPath = "Panel",
                LayerId = 2,
                Type = UiElementType.Panel,
                Rect = new UiRect(0d, 0d, 1920d, 1080d),
                SectionKind = "open"
            };

            var background = new UiNode
            {
                Id = StableId.NodeId("Panel/bg", 3),
                Name = "bg",
                LayerPath = "Panel/bg",
                LayerId = 3,
                Type = UiElementType.Image,
                Role = UiRole.Background,
                Rect = new UiRect(0d, 0d, 1920d, 1080d),
                ResourceId = document.Resources[0].Id,
                Border = new UiBorder(12, 12, 12, 12)
            };

            var title = new UiNode
            {
                Id = StableId.NodeId("Panel/标题", 4),
                Name = "标题",
                LayerPath = "Panel/标题",
                LayerId = 4,
                Type = UiElementType.Text,
                Rect = new UiRect(760d, 120d, 400d, 80d),
                Text = new UiTextInfo
                {
                    HasValue = true,
                    Content = "欢迎回来",
                    FontSize = 48d,
                    Color = UiColor.FromBytes(255, 255, 255, 255),
                    FontKey = "SourceHanSans-Bold",
                    Align = "center",
                    Tracking = 1.5d
                }
            };
            title.Effects.Add(new UiEffect
            {
                Kind = "stroke",
                Size = 2d,
                Color = UiColor.FromBytes(0, 0, 0, 255),
                Opacity = 0.8d
            });
            title.Tags["type"] = "text";

            root.AddChild(background);
            root.AddChild(title);
            document.Root = root;

            document.Stats["parseMs"] = "12";
            document.Report(DiagnosticSeverity.Warning, "PSD-0001", "示例警告", title);
            return document;
        }
    }

    public class ContractJsonTests
    {
        [Fact]
        public void 契约往返保持一致()
        {
            UiDocument source = SampleDocument.Create();
            string text = ContractJson.ToJsonText(source);
            UiDocument parsed = ContractJson.Parse(text);

            Assert.Equal(source.SchemaVersion, parsed.SchemaVersion);
            Assert.Equal(source.Document.Name, parsed.Document.Name);
            Assert.Equal(source.Document.Height, parsed.Document.Height);
            Assert.Equal(source.Resources.Count, parsed.Resources.Count);
            Assert.Equal("login", parsed.Resources[0].Module);
            Assert.Equal("sprite/login/bg_1920x1080_abcd1234.png", parsed.Resources[0].ContractPath);
            Assert.True(parsed.Resources[0].Border.Equals(new UiBorder(12, 12, 12, 12)));

            Assert.Equal(2, parsed.Root.Children.Count);
            Assert.Equal("Panel", parsed.Root.Name);
            Assert.Equal(UiElementType.Panel, parsed.Root.Type);
            Assert.Equal("open", parsed.Root.SectionKind);

            UiNode background = parsed.Root.Children[0];
            Assert.Equal(UiRole.Background, background.Role);
            Assert.Equal(source.Root.Children[0].ResourceId, background.ResourceId);

            UiNode title = parsed.Root.Children[1];
            Assert.True(title.Text.HasValue);
            Assert.Equal("欢迎回来", title.Text.Content);
            Assert.Equal(48d, title.Text.FontSize);
            Assert.Equal(1.5d, title.Text.Tracking);
            Assert.Equal(1d, title.Text.Color.R, 6);
            Assert.Equal(1d, title.Text.Color.A, 6);
            Assert.Single(title.Effects);
            Assert.Equal("stroke", title.Effects[0].Kind);
            Assert.Equal("text", title.Tags["type"]);

            Assert.Single(parsed.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Warning, parsed.Diagnostics[0].Severity);
            Assert.Equal("PSD-0001", parsed.Diagnostics[0].Code);
            Assert.Equal(title.LayerPath, parsed.Diagnostics[0].LayerPath);
            Assert.Equal("12", parsed.Stats["parseMs"]);
        }

        [Fact]
        public void 同一份文档两次序列化完全一致()
        {
            UiDocument document = SampleDocument.Create();
            Assert.Equal(ContractJson.ToJsonText(document), ContractJson.ToJsonText(document));
        }

        [Fact]
        public void 顶层字段顺序稳定()
        {
            string text = ContractJson.ToJsonText(SampleDocument.Create());
            int schema = text.IndexOf("schemaVersion", System.StringComparison.Ordinal);
            int generator = text.IndexOf("generator", System.StringComparison.Ordinal);
            int doc = text.IndexOf("\"document\"", System.StringComparison.Ordinal);
            int resources = text.IndexOf("\"resources\"", System.StringComparison.Ordinal);
            int diagnostics = text.IndexOf("\"diagnostics\"", System.StringComparison.Ordinal);
            int root = text.IndexOf("\"root\"", System.StringComparison.Ordinal);

            Assert.True(schema >= 0 && schema < generator);
            Assert.True(generator < doc);
            Assert.True(doc < resources);
            Assert.True(resources < diagnostics);
            Assert.True(diagnostics < root);
        }

        [Fact]
        public void 节点枚举以可读字符串输出()
        {
            UiDocument document = SampleDocument.Create();
            JsonValue json = ContractJson.ToJson(document);

            Assert.Equal("panel", json["root"]["type"].AsString());
            Assert.Equal("background", json["root"]["children"][0]["role"].AsString());
            Assert.Equal("tmp-text", UiElementType.TmpText.ToContract());
            Assert.Equal(UiElementType.TmpInputField, UiNaming.ParseElementType("tmp-input-field"));
            Assert.Equal(UiElementType.None, UiNaming.ParseElementType("不存在的类型"));
            Assert.Equal(UiRole.Preview, UiNaming.ParseRole("preview"));
        }

        [Fact]
        public void 空文档不抛异常()
        {
            UiDocument document = new UiDocument();
            string text = ContractJson.ToJsonText(document);
            UiDocument parsed = ContractJson.Parse(text);

            Assert.Null(parsed.Root);
            Assert.Empty(parsed.Resources);
            Assert.False(parsed.HasErrors);
        }
    }

    public class StableIdTests
    {
        [Fact]
        public void 相同输入产生相同ID()
        {
            Assert.Equal(StableId.NodeId("Panel/btn", 7), StableId.NodeId("Panel/btn", 7));
            Assert.NotEqual(StableId.NodeId("Panel/btn", 7), StableId.NodeId("Panel/btn", 8));
            Assert.NotEqual(StableId.NodeId("Panel/btn", 7), StableId.NodeId("Panel/btn2", 7));
            Assert.StartsWith("n", StableId.NodeId("Panel/btn", 7));
        }

        [Fact]
        public void 资源ID带类型前缀且长度稳定()
        {
            string sprite = StableId.ResourceId(UiResourceKind.Sprite, "bg");
            string texture = StableId.ResourceId(UiResourceKind.Texture, "bg");

            Assert.StartsWith("r", sprite);
            Assert.NotEqual(sprite, texture);
            Assert.Equal(16, StableId.Hash("anything").Length);
        }

        [Fact]
        public void 文件名过滤非法字符并保留中文()
        {
            string hash = StableId.HashBytes(new byte[] { 1, 2, 3 });
            Assert.Equal("按钮_64x32_" + StableId.ShortHash(hash) + ".png", StableId.FileName("按钮", hash, 64, 32));
            Assert.Equal("a_b_1x1_" + StableId.ShortHash(hash) + ".png", StableId.FileName("a/b", hash, 1, 1));
            Assert.Equal("layer_1x1_" + StableId.ShortHash(hash) + ".png", StableId.FileName("   ", hash, 1, 1));
        }

        [Fact]
        public void 内容哈希对字节变化敏感()
        {
            byte[] a = { 1, 2, 3 };
            byte[] b = { 1, 2, 4 };

            Assert.Equal(StableId.HashBytes(a), StableId.HashBytes(new byte[] { 1, 2, 3 }));
            Assert.NotEqual(StableId.HashBytes(a), StableId.HashBytes(b));
        }
    }
}
