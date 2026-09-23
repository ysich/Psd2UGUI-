using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Imaging;
using Psd2Ugui.Core.Json;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    /// <summary>
    /// 交付前预检：查的都是「后面几步会真的出问题、但默认流程不会报错」的情况。
    /// 这里把每条检查都钉死，免得规则悄悄漂移。
    /// </summary>
    public class PreflightTests
    {
        [Fact]
        public void 正常文档不产生错误()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image));

            int added = Preflight.Run(document, null);

            Assert.Equal(0, added);
            Assert.Equal(0, document.CountSeverity(DiagnosticSeverity.Error));
        }

        [Fact]
        public void 画布尺寸非法报错误()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image));
            document.Document.Width = 0;

            Preflight.Run(document, null);

            Assert.Equal(DiagnosticSeverity.Error, Find(document, "preflight.empty-canvas").Severity);
        }

        [Fact]
        public void 空画布给提示()
        {
            UiDocument document = Doc();

            Preflight.Run(document, null);

            Assert.NotNull(Find(document, "preflight.empty-document"));
            Assert.Equal(0, document.CountSeverity(DiagnosticSeverity.Error));
        }

        [Fact]
        public void 重名资源给提示()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image));
            var plan = new ExportPlan();
            plan.Sprites.Add(Sprite("Mark", "hash-a"));
            plan.Sprites.Add(Sprite("Mark", "hash-b"));

            Preflight.Run(document, plan);

            UiDiagnostic diagnostic = Find(document, "preflight.duplicate-sprite-name");
            Assert.NotNull(diagnostic);
            Assert.Contains("Mark", diagnostic.Message);
        }

        [Fact]
        public void 内容相同的重名只算一张图()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image));
            var plan = new ExportPlan();
            plan.Sprites.Add(Sprite("Mark", "hash-a"));
            plan.Sprites.Add(Sprite("Mark", "hash-a"));

            Preflight.Run(document, plan);

            Assert.Null(Find(document, "preflight.duplicate-sprite-name"));
        }

        [Theory]
        [InlineData(4, 2, true)]
        [InlineData(5, 2, false)]
        [InlineData(6, 2, false)]
        public void 九宫把中间区切没了才报警(int size, int border, bool degenerate)
        {
            UiDocument document = Doc(Node("Frame", UiElementType.Image));
            var plan = new ExportPlan();
            SpriteExport sprite = Sprite("Frame", "hash-a");
            sprite.Bitmap = new Bitmap(size, size);
            sprite.Border = new UiBorder(border, border, border, border);
            plan.Sprites.Add(sprite);

            Preflight.Run(document, plan);

            Assert.Equal(degenerate, Find(document, "preflight.slice-degenerate") != null);
        }

        [Fact]
        public void 超过工程贴图上限要报警()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image));
            var plan = new ExportPlan();
            SpriteExport sprite = Sprite("Bg", "hash-a");
            sprite.Bitmap = new Bitmap(2049, 100);
            plan.Sprites.Add(sprite);

            Preflight.Run(document, plan, new PreflightOptions { MaxTextureSize = 2048 });

            Assert.NotNull(Find(document, "preflight.oversized-sprite"));
        }

        [Fact]
        public void 没超过上限就不吭声()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image));
            var plan = new ExportPlan();
            SpriteExport sprite = Sprite("Bg", "hash-a");
            sprite.Bitmap = new Bitmap(2048, 2048);
            plan.Sprites.Add(sprite);

            Preflight.Run(document, plan, new PreflightOptions { MaxTextureSize = 2048 });

            Assert.Null(Find(document, "preflight.oversized-sprite"));
        }

        [Fact]
        public void 工程里没有这个字体就报警()
        {
            UiDocument document = Doc(Text("Title", "AdobeHeitiStd-Regular"));

            Preflight.Run(document, null, new PreflightOptions { KnownFonts = new List<string>() });

            UiDiagnostic diagnostic = Find(document, "preflight.font-missing");
            Assert.NotNull(diagnostic);
            Assert.Contains("AdobeHeitiStd-Regular", diagnostic.Message);
        }

        [Fact]
        public void 字体在工程里就不吭声()
        {
            UiDocument document = Doc(Text("Title", "AdobeHeitiStd-Regular"));

            Preflight.Run(document, null,
                new PreflightOptions { KnownFonts = new List<string> { "AdobeHeitiStd-Regular" } });

            Assert.Null(Find(document, "preflight.font-missing"));
            Assert.Null(Find(document, "preflight.font-unknown"));
        }

        [Fact]
        public void 没填字体名时只给提示()
        {
            UiDocument document = Doc(Text("Title", string.Empty));

            Preflight.Run(document, null, new PreflightOptions { KnownFonts = new List<string>() });

            Assert.NotNull(Find(document, "preflight.font-unknown"));
            Assert.Equal(0, document.CountSeverity(DiagnosticSeverity.Warning));
        }

        [Fact]
        public void 不传字体表就不查字体()
        {
            UiDocument document = Doc(Text("Title", "随便什么字体"));

            Preflight.Run(document, null);

            Assert.Null(Find(document, "preflight.font-missing"));
        }

        [Fact]
        public void 被整棵忽略的子树要提示()
        {
            UiNode group = Node("Legacy", UiElementType.Ignore);
            group.AddChild(Node("A", UiElementType.Image));
            group.AddChild(Node("B", UiElementType.Image));
            UiDocument document = Doc(group);

            Preflight.Run(document, null);

            UiDiagnostic diagnostic = Find(document, "preflight.ignored-subtree");
            Assert.NotNull(diagnostic);
            Assert.Contains("2", diagnostic.Message);
        }

        [Fact]
        public void 没有子节点的忽略图层不提示()
        {
            UiDocument document = Doc(Node("Legacy", UiElementType.Ignore));

            Preflight.Run(document, null);

            Assert.Null(Find(document, "preflight.ignored-subtree"));
        }

        [Fact]
        public void 猜出来的控件类型要提示()
        {
            UiNode button = Node("ButtonBule", UiElementType.Button);
            button.Tags["type-source"] = "inferred";
            UiDocument document = Doc(button);

            Preflight.Run(document, null);

            Assert.NotNull(Find(document, "preflight.inferred-control"));
        }

        [Fact]
        public void 标签指定的控件类型不提示()
        {
            UiNode button = Node("ButtonBule", UiElementType.Button);
            button.Tags["type-source"] = "tag";
            UiDocument document = Doc(button);

            Preflight.Run(document, null);

            Assert.Null(Find(document, "preflight.inferred-control"));
        }

        [Fact]
        public void 猜出来的图片类型不提示()
        {
            UiNode image = Node("Logo", UiElementType.Image);
            image.Tags["type-source"] = "inferred";
            UiDocument document = Doc(image);

            Preflight.Run(document, null);

            Assert.Null(Find(document, "preflight.inferred-control"));
        }

        [Fact]
        public void 语义层给每个节点记下类型来源()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Solid(builder, "Btn.img");

            UiDocument document = NodeBuilder.Build(PsdParser.Read(builder.Build()));

            UiNode node = null;
            foreach (UiNode item in document.Nodes())
            {
                if (item.Name == "Btn")
                {
                    node = item;
                }
            }

            Assert.NotNull(node);
            Assert.Equal("tag", node.Tags["type-source"]);
        }

        [Fact]
        public void 报告带上节点资源与诊断明细()
        {
            UiDocument document = Doc(Text("Title", string.Empty));
            document.Document.FileName = "Login.psd";
            document.Document.Module = "login";
            var plan = new ExportPlan { Module = "login" };
            plan.Sprites.Add(Sprite("Bg", "hash-a"));
            plan.Sprites[0].Bitmap = new Bitmap(10, 20);
            document.Resources.Add(new UiResource { Id = "r1", Name = "Bg" });

            Preflight.Run(document, plan);
            ImportReport report = ImportReport.From(document, plan);
            report.SourceBytes = 4096;
            report.ParseMilliseconds = 12.5d;
            report.PlanMilliseconds = 3.5d;

            Assert.Equal("Login.psd", report.SourceFile);
            Assert.Equal("login", report.Module);
            Assert.Equal(1, report.Sprites);
            Assert.Equal(1, report.Nodes);
            Assert.Equal(200, report.SpritePixels);
            Assert.Equal(16d, report.TotalMilliseconds);
            Assert.Equal(document.Diagnostics.Count, report.Diagnostics.Count);
            Assert.Equal(document.CountSeverity(DiagnosticSeverity.Info), report.Infos);
            Assert.Contains("Login.psd", report.BuildSummary());
        }

        [Fact]
        public void 报告的JSON能被读回来()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image));
            document.Document.FileName = "Login.psd";
            var plan = new ExportPlan();
            plan.Sprites.Add(Sprite("Bg", "hash-a"));

            ImportReport report = ImportReport.From(document, plan);
            string text = report.ToJsonText();

            JsonValue root = JsonParser.Parse(text);
            Assert.Equal("1.0.0", root["version"].AsString(string.Empty));
            Assert.Equal("Login.psd", root["source"]["file"].AsString(string.Empty));
            Assert.Equal(100, root["source"]["width"].AsInt());
            Assert.Equal(1, root["counts"]["sprites"].AsInt());
            Assert.Equal(0, root["diagnostics"]["error"].AsInt());
            Assert.Equal(document.Stats["nodes"], root["stats"]["nodes"].AsString(string.Empty));
        }

        private static UiDiagnostic Find(UiDocument document, string code)
        {
            for (int i = 0; i < document.Diagnostics.Count; i++)
            {
                if (document.Diagnostics[i].Code == code)
                {
                    return document.Diagnostics[i];
                }
            }

            return null;
        }

        private static SpriteExport Sprite(string name, string hash)
        {
            return new SpriteExport
            {
                Name = name,
                FileName = name + ".png",
                ContentHash = hash,
                Bitmap = new Bitmap(8, 8),
                Border = new UiBorder(0, 0, 0, 0),
                ResourceId = StableId.ResourceId(UiResourceKind.Sprite, name + hash)
            };
        }

        private static UiDocument Doc(params UiNode[] children)
        {
            var document = new UiDocument();
            document.Document.Name = "Test";
            document.Document.FileName = "Test.psd";
            document.Document.Width = 100;
            document.Document.Height = 200;
            document.Document.LayerCount = children.Length;
            document.Root = new UiNode
            {
                Id = "root",
                Name = "Test",
                Type = UiElementType.Group,
                Rect = new UiRect(0d, 0d, 100d, 200d)
            };

            for (int i = 0; i < children.Length; i++)
            {
                document.Root.AddChild(children[i]);
            }

            document.Stats["nodes"] = children.Length.ToString();
            document.Stats["groups"] = "0";
            return document;
        }

        private static UiNode Node(string name, UiElementType type)
        {
            return new UiNode
            {
                Id = "n-" + name,
                Name = name,
                LayerPath = name,
                Type = type,
                Rect = new UiRect(0d, 0d, 10d, 10d)
            };
        }

        private static UiNode Text(string name, string fontName)
        {
            UiNode node = Node(name, UiElementType.TmpText);
            node.Text = new UiTextInfo
            {
                HasValue = true,
                Content = name,
                FontSize = 20d,
                FontName = fontName
            };
            return node;
        }

        private static void Solid(PsdFixtureBuilder builder, string name)
        {
            LayerSpec layer = builder.AddLayer(name);
            layer.Left = 0;
            layer.Top = 0;
            layer.Right = 8;
            layer.Bottom = 8;
            int count = 64;
            var channel = new byte[count];
            var alpha = new byte[count];
            for (int i = 0; i < count; i++)
            {
                channel[i] = (byte)(20 + i);
                alpha[i] = 255;
            }

            layer.WithChannel(0, PsdCompression.Raw, channel)
                .WithChannel(1, PsdCompression.Raw, channel)
                .WithChannel(2, PsdCompression.Raw, channel)
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, alpha);
        }
    }
}
