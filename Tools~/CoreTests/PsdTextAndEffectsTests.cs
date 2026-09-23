using System;
using System.Collections.Generic;
using System.Text;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Psd;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    public class EngineDataParserTests
    {
        private static EngineDataNode Parse(string text)
        {
            return EngineDataParser.Parse(Encoding.ASCII.GetBytes(text), 0, Encoding.ASCII.GetByteCount(text));
        }

        [Fact]
        public void 解析字典数组与标量()
        {
            EngineDataNode root = Parse("<< /A 1 /B true /C (hi) /D [ 1 2 3 ] /E << /F 2.5 >> >>");

            Assert.True(root.IsDict);
            Assert.Equal(1d, root.GetNumber("A"));
            Assert.True(root.Get("B").Bool);
            Assert.Equal("hi", root.GetString("C"));
            Assert.True(root.Get("D").IsArray);
            Assert.Equal(3, root.Get("D").Array.Count);
            Assert.Equal(2.5d, root.Path("E", "F").AsNumber());
            Assert.Null(root.Get("missing"));
        }

        [Fact]
        public void 名字节点保留斜杠后的标识符()
        {
            EngineDataNode root = Parse("<< /Kind /FrameType >>");
            Assert.Equal(EngineDataKind.Name, root.Get("Kind").Kind);
            Assert.Equal("FrameType", root.Get("Kind").AsString());
        }

        [Fact]
        public void 键带引号时也能取到()
        {
            EngineDataNode root = Parse("<< /'Editor' << /Text (x) >> >>");
            Assert.Equal("x", root.Path("Editor", "Text").AsString());
        }

        [Fact]
        public void 括号里的转义字符按普通字节处理()
        {
            EngineDataNode root = Parse("<< /T (a\\)b) >>");
            Assert.Equal("a)b", root.GetString("T"));
        }

        [Fact]
        public void 空数据返回Null节点()
        {
            Assert.Equal(EngineDataKind.Null, EngineDataParser.Parse(new byte[0], 0, 0).Kind);
            Assert.Equal(EngineDataKind.Null, EngineDataParser.Parse(null, 0, 0).Kind);
        }

        [Fact]
        public void 文本按UTF16BE的BOM解码()
        {
            var raw = new List<byte> { 0xFE, 0xFF, 0x00, 0x4E, 0x00, 0x61, 0x00, 0x6D, 0x00, 0x65 };
            Assert.Equal("Name", EngineDataParser.DecodeText(raw));
        }

        [Fact]
        public void 文本不带BOM时按ASCII解码()
        {
            var raw = new List<byte>(Encoding.ASCII.GetBytes("Impact"));
            Assert.Equal("Impact", EngineDataParser.DecodeText(raw));
        }
    }

    public class PsdDescriptorTests
    {
        [Fact]
        public void 解析对象描述符的各类值()
        {
            byte[] body = Desc.ObjectBody("null",
                Desc.Entry("On  ", Desc.Bool(true)),
                Desc.Entry("Sz  ", Desc.Long(12)),
                Desc.Entry("Bl  ", Desc.Double(0.25)),
                Desc.Entry("Opct", Desc.Unit("#Prc", 75d)),
                Desc.Entry("Md  ", Desc.Enum("BlnM", "Mltp")),
                Desc.Entry("Nm  ", Desc.Text("名字")),
                Desc.Entry("Nested", Desc.Object("RGBC", Desc.Entry("Rd  ", Desc.Double(1d)))));

            var reader = new PsdBinaryReader(body);
            PsdDescriptor descriptor = PsdDescriptorReader.ReadDescriptor(reader);

            Assert.Equal("null", descriptor.ClassId);
            Assert.True(descriptor.GetBool("On  "));
            Assert.Equal(12d, descriptor.GetNumber("Sz  "));
            Assert.Equal(0.25d, descriptor.GetNumber("Bl  "));
            Assert.Equal(75d, descriptor.GetNumber("Opct"));
            Assert.Equal("Mltp", descriptor.GetEnum("Md  "));
            Assert.Equal("名字", descriptor.GetText("Nm  "));
            Assert.Equal(1d, descriptor.Get("Nested").Object.GetNumber("Rd  "));
        }

        [Fact]
        public void 旧格式描述符走回退分支()
        {
            byte[] body = Desc.LegacyObjectBody("null", Desc.Entry("Rd  ", Desc.Double(0.5)));

            var reader = new PsdBinaryReader(body);
            PsdDescriptor descriptor = PsdDescriptorReader.ReadDescriptor(reader);

            Assert.Equal("null", descriptor.ClassId);
            Assert.Equal(0.5d, descriptor.GetNumber("Rd  "));
        }

        [Fact]
        public void 未支持的值类型抛解析异常()
        {
            byte[] body = Desc.ObjectBody("null", Desc.Entry("Bad ", Encoding.ASCII.GetBytes("zzzz")));
            var reader = new PsdBinaryReader(body);

            Assert.Throws<PsdParseException>(() => PsdDescriptorReader.ReadDescriptor(reader));
        }

        [Fact]
        public void 单通道颜色按0到1归一化后转成Byte()
        {
            var descriptor = new PsdDescriptor { ClassId = "RGBC" };
            descriptor.Add("Rd  ", new PsdValue { TypeId = "doub", Number = 1d });
            descriptor.Add("Grn ", new PsdValue { TypeId = "doub", Number = 0.5d });
            descriptor.Add("Bl  ", new PsdValue { TypeId = "doub", Number = 0d });

            UiColor color = descriptor.ToColor(UiColor.White);

            Assert.Equal(1d, color.R);
            Assert.Equal(0.5d, color.G);
            Assert.Equal(0d, color.B);
        }
    }

    public class PsdTextLayerTests
    {
        private const string EngineData =
            "<< /EngineDict << " +
            "/Editor << /Text (Hello) >> " +
            "/StyleRun << /RunArray [ << /StyleSheet << /StyleSheetData << " +
            "/Font 1 /FontSize 42 /Tracking -20 /Leading 50 /AutoLeading false " +
            "/FillColor << /Type 1 /Values [ 1 0 0.5 1 ] >> " +
            "/StrokeColor << /Type 1 /Values [ 1 1 1 1 ] >> >> >> >> ] >> " +
            "/ParagraphRun << /RunArray [ << /ParagraphSheet << /Properties << /Justification 2 >> >> >> ] >> " +
            ">> " +
            "/ResourceDict << /FontSet [ << /Name (ArialMT) >> << /Name (Impact) >> ] >> >>";

        private static byte[] TypeToolPayload(string text, string engineData)
        {
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

        private static PsdFile ParseTextLayer(string text, string engineData)
        {
            var builder = new PsdFixtureBuilder();
            LayerSpec layer = builder.AddLayer("Title");
            layer.Right = 40;
            layer.Bottom = 20;
            layer.WithTag("TySh", TypeToolPayload(text, engineData))
                .WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            return PsdParser.Read(builder.Build());
        }

        [Fact]
        public void 文本层解析出内容与样式()
        {
            PsdLayer layer = ParseTextLayer("Hello", EngineData).Layers[0];

            Assert.NotNull(layer.Text);
            Assert.Equal("Hello", layer.Text.Text);
            Assert.Equal(42d, layer.Text.FontSize);
            Assert.Equal("Impact", layer.Text.FontName);
            Assert.Equal("center", layer.Text.Justification);
            Assert.True(layer.Text.HasColor);
            // FillColor 的值顺序是 ARGB
            Assert.Equal("#0080ffff", layer.Text.Color.ToHexRgba());
            Assert.True(layer.Text.HasStrokeColor);
            Assert.Equal("#ffffffff", layer.Text.StrokeColor.ToHexRgba());
            Assert.Equal(10d, layer.Text.TransformX);
            Assert.Equal(20d, layer.Text.TransformY);
        }

        [Fact]
        public void 文本以回车结尾时会被裁掉()
        {
            PsdLayer layer = ParseTextLayer("Hello", EngineData.Replace("(Hello)", "(Hello\r)")).Layers[0];
            Assert.Equal("Hello", layer.Text.Text);
        }

        [Fact]
        public void 没有FillColor时保留默认白色()
        {
            string data = EngineData.Replace("/FillColor << /Type 1 /Values [ 1 0 0.5 1 ] >> ", string.Empty);
            PsdLayer layer = ParseTextLayer("Hi", data).Layers[0];

            Assert.False(layer.Text.HasColor);
            Assert.Equal(UiColor.White, layer.Text.Color);
        }

        [Fact]
        public void 颜色以数组形式出现时也能解析()
        {
            string data = EngineData.Replace("/FillColor << /Type 1 /Values [ 1 0 0.5 1 ] >>", "/FillColor [ 1 0 0.5 1 ]");
            PsdLayer layer = ParseTextLayer("Hi", data).Layers[0];

            Assert.Equal("#0080ffff", layer.Text.Color.ToHexRgba());
        }

        [Fact]
        public void 缺少文本引擎数据时记录警告()
        {
            var builder = new PsdFixtureBuilder();
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteU16(payload, 1);
            for (int i = 0; i < 6; i++)
            {
                PsdFixtureBuilder.WriteF64(payload, i == 0 || i == 3 ? 1d : 0d);
            }

            PsdFixtureBuilder.WriteU16(payload, 50);
            PsdFixtureBuilder.WriteI32(payload, 16);
            payload.AddRange(Desc.ObjectBody("null", Desc.Entry("Txt ", Desc.Text("NoEngine"))));

            builder.AddLayer("bare").WithTag("TySh", payload.ToArray())
                .WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Equal("NoEngine", file.Layers[0].Text.Text);
            Assert.Contains(file.Warnings, warning => warning.Contains("缺少文本引擎数据"));
        }

        [Fact]
        public void 纯色填充层解析出颜色()
        {
            var builder = new PsdFixtureBuilder();
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, 16);
            payload.AddRange(Desc.ObjectBody("null",
                Desc.Entry("Clr ", Desc.Object("RGBC",
                    Desc.Entry("Rd  ", Desc.Double(0.5d)),
                    Desc.Entry("Grn ", Desc.Double(0.25d)),
                    Desc.Entry("Bl  ", Desc.Double(0d))))));

            var layer = builder.AddLayer("BgColor");
            layer.WithTag("SoCo", payload.ToArray()).WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdLayer parsed = PsdParser.Read(builder.Build()).Layers[0];

            Assert.True(parsed.IsFillLayer);
            Assert.True(parsed.HasSolidFill);
            Assert.Equal("#804000ff", parsed.SolidFill.ToHexRgba());
        }
    }

    public class PsdEffectsReaderTests
    {
        private static byte[] Effect(string classId, bool enabled, bool present)
        {
            return Desc.Object(classId,
                Desc.Entry("enab", Desc.Bool(enabled)),
                Desc.Entry("present", Desc.Bool(present)),
                Desc.Entry("blur", Desc.Unit("#Pxl", 3d)),
                Desc.Entry("Md  ", Desc.Enum("BlnM", "Nrml")));
        }

        private static PsdFile ParseEffects(params byte[][] entries)
        {
            var builder = new PsdFixtureBuilder();
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, 1);
            PsdFixtureBuilder.WriteI32(payload, 16);
            payload.AddRange(Desc.ObjectBody("null", entries));

            builder.AddLayer("fx").WithTag("lfx2", payload.ToArray())
                .WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            return PsdParser.Read(builder.Build());
        }

        [Fact]
        public void 只保留enab与present都为真的效果()
        {
            PsdLayer layer = ParseEffects(
                Desc.Entry("DrSh", Effect("DrSh", true, true)),
                Desc.Entry("OrGl", Effect("OrGl", false, true)),
                Desc.Entry("IrGl", Effect("IrGl", true, false)),
                Desc.Entry("ebbl", Effect("ebbl", true, true))).Layers[0];

            Assert.Equal(2, layer.Effects.Count);
            Assert.Equal("drop-shadow", layer.Effects[0].Kind);
            Assert.Equal("bevel", layer.Effects[1].Kind);
            Assert.True(layer.Effects[0].Enabled);
            Assert.Equal(3d, layer.Effects[0].Size);
        }

        [Fact]
        public void Multi列表里的效果同样识别()
        {
            PsdLayer layer = ParseEffects(
                Desc.Entry("innerShadowMulti", Desc.List(Effect("IrSh", true, true))),
                Desc.Entry("gradientFillMulti", Desc.List(
                    Effect("GrFl", false, true),
                    Effect("GrFl", true, true)))).Layers[0];

            Assert.Equal(2, layer.Effects.Count);
            Assert.Equal("inner-shadow", layer.Effects[0].Kind);
            Assert.Equal("gradient-fill", layer.Effects[1].Kind);
        }

        [Fact]
        public void 未支持的效果类只记录警告()
        {
            PsdFile file = ParseEffects(Desc.Entry("WeIr", Effect("WeIr", true, true)));

            Assert.Empty(file.Layers[0].Effects);
            Assert.Contains(file.Warnings, warning => warning.Contains("未支持的效果类"));
        }

        [Fact]
        public void 未知的附加信息标签记入UnknownTags()
        {
            var builder = new PsdFixtureBuilder();
            builder.AddLayer("tagged").WithTag("zzzz", new byte[] { 1, 2, 3, 4 })
                .WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Contains("zzzz", file.Layers[0].UnknownTags);

        }

        [Fact]
        public void 效果块损坏时记录警告但不影响图层()
        {
            var builder = new PsdFixtureBuilder();
            builder.AddLayer("broken").WithTag("lfx2", new byte[] { 0, 0, 0, 1, 0, 0 })
                .WithChannel(0, PsdCompression.Raw, new byte[] { 1 });

            PsdFile file = PsdParser.Read(builder.Build());

            Assert.Single(file.Layers);
            Assert.NotEmpty(file.Warnings);
        }
    }
}
