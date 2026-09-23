using System.IO;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Psd2Ugui.Editor.Import;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    /// <summary>
    /// 资源身份映射（`.psd2ugui.json`）：重新导出时靠它决定哪张图可以原样复用。
    /// 它是 Step 8 增量更新的地基，所以放在能脱离 Unity 的测试里。
    /// </summary>
    public class ManifestTests
    {
        private const string MarkerName = "</Layer group>";

        [Fact]
        public void 清单往返后内容不变()
        {
            ExportPlan plan = BuildPlan();
            var manifest = new Psd2UguiManifest();
            manifest.Update(plan, "Login.psd");

            Psd2UguiManifest loaded = Psd2UguiManifest.FromJsonText(manifest.ToJsonText());

            Assert.Equal(Psd2UguiManifest.CurrentVersion, loaded.Version);
            Assert.Equal(plan.Module, loaded.Module);
            Assert.Equal("Login.psd", loaded.SourceFileName);
            Assert.Equal(plan.Sprites.Count, loaded.Resources.Count);
            foreach (SpriteExport sprite in plan.Sprites)
            {
                Psd2UguiManifest.ManifestEntry entry = loaded.Resources[sprite.ResourceId];
                Assert.Equal(sprite.Name, entry.Name);
                Assert.Equal(sprite.FileName, entry.File);
                Assert.Equal(sprite.ContentHash, entry.ContentHash);
                Assert.Equal(sprite.Bitmap.Width, entry.Width);
                Assert.Equal(sprite.Bitmap.Height, entry.Height);
                Assert.Equal(sprite.SourceLayerId, entry.SourceLayerId);
                Assert.Equal(sprite.SourceLayerPath, entry.SourceLayerPath);
                Assert.Equal(Psd2UguiManifest.BorderKeyOf(sprite.Border), entry.BorderKey);
            }
        }

        [Fact]
        public void 同样的计划重复导出都算最新()
        {
            ExportPlan plan = BuildPlan();
            var manifest = new Psd2UguiManifest();
            manifest.Update(plan, "Login.psd");

            foreach (SpriteExport sprite in plan.Sprites)
            {
                Assert.True(manifest.IsUpToDate(sprite));
            }
        }

        [Fact]
        public void 内容变了就不再是最新()
        {
            ExportPlan plan = BuildPlan();
            var manifest = new Psd2UguiManifest();
            manifest.Update(plan, "Login.psd");

            SpriteExport sprite = plan.Sprites[0];
            string originalHash = sprite.ContentHash;
            sprite.ContentHash = "changed";

            Assert.False(manifest.IsUpToDate(sprite));

            sprite.ContentHash = originalHash;
            Assert.True(manifest.IsUpToDate(sprite));
        }

        [Fact]
        public void 九宫变了就不再是最新()
        {
            ExportPlan plan = BuildPlan();
            var manifest = new Psd2UguiManifest();
            manifest.Update(plan, "Login.psd");

            SpriteExport sprite = plan.Sprites[0];
            UiBorder original = sprite.Border;
            sprite.Border = new UiBorder(4, 4, 4, 4);

            Assert.False(manifest.IsUpToDate(sprite));

            sprite.Border = original;
            Assert.True(manifest.IsUpToDate(sprite));
        }

        [Fact]
        public void 没登记过的资源不算最新()
        {
            var manifest = new Psd2UguiManifest();

            Assert.False(manifest.IsUpToDate(new SpriteExport { ResourceId = "r1", ContentHash = "h" }));
        }

        [Fact]
        public void 重新导出后能列出已经用不到的旧资源()
        {
            ExportPlan first = BuildPlan();
            var manifest = new Psd2UguiManifest();
            manifest.Update(first, "Login.psd");
            Assert.Equal(2, manifest.Resources.Count);

            // 第二次只留一张图
            var second = new ExportPlan();
            second.AssetRoot = first.AssetRoot;
            second.Module = first.Module;
            second.Sprites.Add(first.Sprites[0]);

            var obsolete = manifest.FindObsolete(second);

            Assert.Single(obsolete);
            Assert.Equal(first.Sprites[1].ResourceId, obsolete[0].Id);
            Assert.Empty(manifest.FindObsolete(first));
        }

        [Fact]
        public void 清单里的资源按ID排序输出稳定()
        {
            ExportPlan plan = BuildPlan();
            var manifest = new Psd2UguiManifest();
            manifest.Update(plan, "Login.psd");
            var again = new Psd2UguiManifest();
            again.Update(plan, "Login.psd");

            Assert.Equal(manifest.ToJsonText(), again.ToJsonText());
        }

        [Fact]
        public void 清单落到磁盘再读回来一致()
        {
            ExportPlan plan = BuildPlan();
            var manifest = new Psd2UguiManifest();
            manifest.Update(plan, "Login.psd");
            string path = Path.Combine(Path.GetTempPath(), "psd2ugui-manifest-test.json");

            try
            {
                manifest.Save(path);
                Psd2UguiManifest loaded = Psd2UguiManifest.Load(path);

                Assert.Equal(plan.Sprites.Count, loaded.Resources.Count);
                Assert.Equal(manifest.ToJsonText(), loaded.ToJsonText());
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void 缺失的清单文件按空清单处理()
        {
            Psd2UguiManifest loaded = Psd2UguiManifest.Load(Path.Combine(Path.GetTempPath(), "not-exists-psd2ugui.json"));

            Assert.Empty(loaded.Resources);
            Assert.Equal(Psd2UguiManifest.CurrentVersion, loaded.Version);
        }

        [Fact]
        public void 九宫边框的键是左底右顶()
        {
            Assert.Equal("1,2,3,4", Psd2UguiManifest.BorderKeyOf(new UiBorder(1, 2, 3, 4)));
            Assert.Equal("0,0,0,0", Psd2UguiManifest.BorderKeyOf(null));
        }

        [Fact]
        public void 解析坏掉的九宫字符串时归零()
        {
            var manifest = Psd2UguiManifest.FromJsonText(
                "{ \"version\": \"1.0.0\", \"resources\": { \"r1\": { \"border\": \"x,1,2\" } } }");

            Assert.Equal("0,0,0,0", manifest.Resources["r1"].BorderKey);
            Assert.Equal("r1", manifest.Resources["r1"].Id);
        }

        private static ExportPlan BuildPlan()
        {
            var builder = new PsdFixtureBuilder { Width = 16, Height = 16 };
            Solid(builder, "Bg.img", 4, 4, 10, 20, 30);
            Solid(builder, "Icon.img", 4, 4, 40, 50, 60);

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            return ExportPlanner.Build(file, document, new ExportOptions { Module = "login" });
        }

        private static LayerSpec Solid(PsdFixtureBuilder builder, string name, int width, int height,
            byte r, byte g, byte b)
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
            for (int i = 0; i < count; i++)
            {
                red[i] = r;
                green[i] = g;
                blue[i] = b;
                alpha[i] = 255;
            }

            layer.WithChannel(0, PsdCompression.Raw, red)
                .WithChannel(1, PsdCompression.Raw, green)
                .WithChannel(2, PsdCompression.Raw, blue)
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, alpha);
            return layer;
        }
    }
}
