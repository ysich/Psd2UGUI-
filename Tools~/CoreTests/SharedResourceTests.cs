using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Psd2Ugui.Editor.Import;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    /// <summary>
    /// 跨界面复用贴图：另一个 PSD 已经导出的图，这次直接拿来用，不再落盘一份。
    ///
    /// 这条链路上的三件事都在 Core 里：共享表怎么读、引用怎么解析、
    /// 以及「复用的图不能被当成失效资源清掉」。
    /// </summary>
    public class SharedResourceTests
    {
        [Fact]
        public void 共享表从身份映射里读出来()
        {
            SharedSpriteTable table = SharedSpriteTable.Load(new[] { ManifestOfLogin() });

            SharedSprite login = table.Find("login", "Bg");

            Assert.NotNull(login);
            Assert.Equal("login", login.Module);
            Assert.StartsWith("Bg_8x8_", login.FileName);
            Assert.EndsWith(".png", login.FileName);
            Assert.Equal(8, login.Width);
            Assert.NotNull(login.Border);
            Assert.False(table.IsEmpty);
        }

        [Fact]
        public void 同模块的共享图优先()
        {
            var table = new SharedSpriteTable();
            table.Add(new SharedSprite { Module = "other", Name = "Bg", FileName = "other.png" });
            table.Add(new SharedSprite { Module = "login", Name = "Bg", FileName = "login.png" });

            Assert.Equal("login.png", table.Find("login", "Bg").FileName);
            Assert.Equal("other.png", table.Find("main", "Bg").FileName);
            Assert.Null(table.Find("login", "不存在"));
        }

        [Fact]
        public void 本文件找不到的引用去共享表里复用()
        {
            ExportOptions options = Options(SharedSpriteTable.Load(new[] { ManifestOfLogin() }));
            UiDocument document = Document("ref Bg.img");
            ExportPlan plan = ExportPlanner.Build(Parse("ref Bg.img"), document, options);

            Assert.Empty(plan.Sprites);
            Assert.Single(plan.Reused);
            Assert.Single(plan.SharedReferences);
            Assert.Empty(plan.ExternalReferences);
            Assert.Equal("login", plan.Reused[0].Module);
            Assert.Equal("1", document.Stats["spritesReused"]);
            Assert.Equal("1", document.Stats["referencesShared"]);
        }

        [Fact]
        public void 复用来的图也在契约资源表里()
        {
            ExportOptions options = Options(SharedSpriteTable.Load(new[] { ManifestOfLogin() }));
            UiDocument document = Document("ref Bg.img");
            ExportPlanner.Build(Parse("ref Bg.img"), document, options);

            UiNode node = PlanNodeNamed(document, "Bg");
            Assert.NotNull(node.ResourceId);
            UiResource resource = document.FindResource(node.ResourceId);
            Assert.NotNull(resource);
            // 落在产出它的那个模块目录下，不在本次导出的模块里
            Assert.Equal("login", resource.Module);
            Assert.EndsWith("/" + resource.FileName, resource.ContractPath);
        }

        [Fact]
        public void 共享表里也没有就报外部引用()
        {
            ExportPlan plan = BuildPlan("ref Bg.img", Options(null));

            Assert.Empty(plan.Sprites);
            Assert.Empty(plan.Reused);
            Assert.Single(plan.ExternalReferences);
        }

        [Fact]
        public void 本文件里的同名图优先于共享表()
        {
            var files = new List<string> { ManifestOfLogin() };
            ExportOptions options = Options(SharedSpriteTable.Load(files));

            // 同一个 PSD 里既有 Bg 也有引用它的节点：用自己这份，不去外面找
            ExportPlan plan = BuildPlan("Bg.img", options, "ref Bg.img");

            Assert.Empty(plan.Reused);
            Assert.Single(plan.Sprites);
            Assert.Single(plan.LocalReferences);
        }

        [Fact]
        public void 复用的共享贴图不会被当成失效资源()
        {
            ExportOptions options = Options(SharedSpriteTable.Load(new[] { ManifestOfLogin() }));
            UiDocument document = Document("ref Bg.img");
            ExportPlan plan = ExportPlanner.Build(Parse("ref Bg.img"), document, options);

            var manifest = new Psd2UguiManifest();
            manifest.Update(plan, "Login2.psd");

            // 记一笔「这张图我还在用」，别的界面重新导出时才知道不能删
            Assert.Single(manifest.Resources);
            Psd2UguiManifest.ManifestEntry entry = manifest.Resources[plan.Reused[0].Id];
            Assert.Equal(plan.Reused[0].FileName, entry.File);
            Assert.True(entry.Shared);

            Assert.Empty(manifest.FindObsolete(plan));

            // 往返一圈之后这份记录还在
            Psd2UguiManifest loaded = Psd2UguiManifest.FromJsonText(manifest.ToJsonText());
            Assert.True(loaded.Resources.ContainsKey(plan.Reused[0].Id));
        }

        [Fact]
        public void 设计稿里没了的图才算失效()
        {
            var manifest = new Psd2UguiManifest();
            manifest.Update(BuildPlan("Bg.img", Options(null)), "Login.psd");

            // 换一份不含 Bg 的计划：上次留下的图这次用不到了
            ExportPlan other = BuildPlan("Icon.img", Options(null));

            Assert.Single(manifest.FindObsolete(other));
        }

        [Fact]
        public void 坏掉的边框字符串归零()
        {
            SharedSpriteTable table = SharedSpriteTable.Load(new[]
            {
                "{ \"module\": \"m\", \"resources\": { \"r1\": { \"name\": \"A\", \"file\": \"a.png\"," +
                " \"border\": \"x,1,2\" } } }"
            });

            Assert.True(table.Find("m", "A").Border.IsZero);
        }

        [Fact]
        public void 身份映射文件名按后缀判定()
        {
            Assert.True(Psd2UguiManifest.IsManifestPath("Assets/PSD2UGUI/manifest/common/Login.psd2ugui.json"));
            Assert.False(Psd2UguiManifest.IsManifestPath("Assets/PSD2UGUI/manifest/common/Login.json"));
            Assert.False(Psd2UguiManifest.IsManifestPath(null));
        }

        [Fact]
        public void 空表不会报错()
        {
            SharedSpriteTable table = SharedSpriteTable.Load(null);

            Assert.True(table.IsEmpty);
            Assert.Null(table.Find("m", "A"));
        }

        /// <summary>登录界面导过一次，留下这份身份映射。</summary>
        private static string ManifestOfLogin()
        {
            var manifest = new Psd2UguiManifest();
            manifest.Update(BuildPlan("Bg.img", new ExportOptions { Module = "login" }), "Login.psd");
            return manifest.ToJsonText();
        }

        private static UiNode PlanNodeNamed(UiDocument document, string name)
        {
            foreach (UiNode node in document.Nodes())
            {
                if (node.Name == name)
                {
                    return node;
                }
            }

            return null;
        }

        private static ExportOptions Options(SharedSpriteTable shared)
        {
            return new ExportOptions { Module = "main", Shared = shared };
        }

        private static ExportPlan BuildPlan(string name, ExportOptions options, params string[] extra)
        {
            var names = new List<string> { name };
            names.AddRange(extra);
            return ExportPlanner.Build(Parse(names.ToArray()), Document(names.ToArray()), options);
        }

        private static UiDocument Document(params string[] names)
        {
            return NodeBuilder.Build(Parse(names));
        }

        private static PsdFile Parse(params string[] names)
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            for (int i = 0; i < names.Length; i++)
            {
                Solid(builder, names[i]);
            }

            return PsdParser.Read(builder.Build());
        }

        private static void Solid(PsdFixtureBuilder builder, string name)
        {
            LayerSpec layer = builder.AddLayer(name);
            layer.Left = 0;
            layer.Top = 0;
            layer.Right = 8;
            layer.Bottom = 8;
            int count = 64;
            var red = new byte[count];
            var green = new byte[count];
            var blue = new byte[count];
            var alpha = new byte[count];
            for (int i = 0; i < count; i++)
            {
                red[i] = (byte)(20 + i);
                green[i] = (byte)(30 + i);
                blue[i] = (byte)(40 + i);
                alpha[i] = 255;
            }

            layer.WithChannel(0, PsdCompression.Raw, red)
                .WithChannel(1, PsdCompression.Raw, green)
                .WithChannel(2, PsdCompression.Raw, blue)
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, alpha);
        }
    }
}
