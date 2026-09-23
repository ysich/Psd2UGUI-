using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Xunit;
using Psd2Ugui.Testing;

namespace Psd2Ugui.CoreTests
{
    /// <summary>
    /// 覆盖表的写入端：编辑器窗口里改的类型/角色要能写成 JSON、再读回来还认。
    /// 读的那一半以前就有，这里补上写的那一半并做一次往返验证。
    /// </summary>
    public class OverrideWriterTests
    {
        [Fact]
        public void 写出来的覆盖表能原样读回来()
        {
            var overrides = new NodeOverrides();
            NodeOverride first = overrides.Set("#12");
            first.HasType = true;
            first.Type = UiElementType.Button;
            first.HasRole = true;
            first.Role = UiRole.Background;

            NodeOverride second = overrides.Set("Panel/Icon");
            second.Name = "图标";
            second.Ignore = true;
            second.HasNineSlice = true;
            second.NineSlice = false;
            second.Parent = "Root";
            second.Resource = "Logo";

            NodeOverrides loaded = NodeOverrides.Parse(overrides.ToJsonText());

            Assert.Equal(2, loaded.Entries.Count);
            NodeOverride typeEntry = loaded.Find(null, 12, null);
            Assert.True(typeEntry.HasType);
            Assert.Equal(UiElementType.Button, typeEntry.Type);
            Assert.True(typeEntry.HasRole);
            Assert.Equal(UiRole.Background, typeEntry.Role);

            NodeOverride pathEntry = loaded.Find("Panel/Icon", 0, "Icon");
            Assert.Equal("图标", pathEntry.Name);
            Assert.True(pathEntry.Ignore);
            Assert.True(pathEntry.HasNineSlice);
            Assert.False(pathEntry.NineSlice);
            Assert.Equal("Root", pathEntry.Parent);
            Assert.Equal("Logo", pathEntry.Resource);
        }

        [Fact]
        public void 同一条键只写一条()
        {
            var overrides = new NodeOverrides();
            overrides.Set("#12").HasType = true;
            overrides.Set("#12").Type = UiElementType.Toggle;

            Assert.Single(overrides.Entries);
            Assert.Equal(UiElementType.Toggle, NodeOverrides.Parse(overrides.ToJsonText()).Entries[0].Type);
        }

        [Fact]
        public void 没设过字段的条目不写出去()
        {
            var overrides = new NodeOverrides();
            overrides.Set("#12");

            Assert.Empty(NodeOverrides.Parse(overrides.ToJsonText()).Entries);
        }

        [Fact]
        public void 移除覆盖后就不再生效()
        {
            var overrides = new NodeOverrides();
            overrides.Set("#12").HasType = true;
            overrides.Set("#12").Type = UiElementType.Button;

            Assert.True(overrides.Remove("#12"));
            Assert.False(overrides.Remove("#12"));
            Assert.Empty(NodeOverrides.Parse(overrides.ToJsonText()).Entries);
        }

        [Fact]
        public void 空覆盖表写出合法的空JSON()
        {
            NodeOverrides loaded = NodeOverrides.Parse(new NodeOverrides().ToJsonText());

            Assert.Empty(loaded.Entries);
            Assert.Empty(NodeOverrides.Parse(null).Entries);
        }

        [Fact]
        public void 写出来的覆盖表能改变解析结果()
        {
            var builder = new PsdFixtureBuilder { Width = 8, Height = 8 };
            Solid(builder, "Btn.img");
            PsdFile file = PsdParser.Read(builder.Build());

            UiDocument before = NodeBuilder.Build(file);
            Assert.Equal(UiElementType.Image, before.Root.Children[0].Type);

            var overrides = new NodeOverrides();
            NodeOverride entry = overrides.Set("#" + before.Root.Children[0].LayerId);
            entry.HasType = true;
            entry.Type = UiElementType.Button;
            entry.HasRole = true;
            entry.Role = UiRole.Background;

            var options = new NodeBuildOptions
            {
                Overrides = NodeOverrides.Parse(overrides.ToJsonText())
            };
            UiDocument after = NodeBuilder.Build(file, options);

            Assert.Equal(UiElementType.Button, after.Root.Children[0].Type);
            Assert.Equal(UiRole.Background, after.Root.Children[0].Role);
            Assert.Equal("override", after.Root.Children[0].Tags["type-source"]);
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
