using System.Collections.Generic;
using Psd2Ugui.Core.Json;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    public class JsonValueTests
    {
        [Fact]
        public void 对象成员保持插入顺序且Set不改变位置()
        {
            JsonValue json = JsonValue.Object();
            json.Set("a", JsonValue.Number(1));
            json.Set("b", JsonValue.Number(2));
            json.Set("a", JsonValue.Number(3));

            Assert.Equal(2, json.Count);
            Assert.Equal("a", json.Members[0].Key);
            Assert.Equal("b", json.Members[1].Key);
            Assert.Equal(3d, json["a"].AsDouble());
        }

        [Fact]
        public void 整数不带小数点小数保留精度()
        {
            Assert.Equal("12", JsonValue.Number(12d).ToJsonString(false));
            Assert.Equal("-3", JsonValue.Number(-3d).ToJsonString(false));
            Assert.Equal("0.5", JsonValue.Number(0.5d).ToJsonString(false));
            Assert.Equal("0", JsonValue.Number(double.NaN).ToJsonString(false));
        }

        [Fact]
        public void 字符串转义与中文保留()
        {
            JsonValue json = JsonValue.String("行1\n行2\t\"引号\"\\反斜杠 中文");
            string text = json.ToJsonString(false);

            Assert.Equal("\"行1\\n行2\\t\\\"引号\\\"\\\\反斜杠 中文\"", text);
        }

        [Fact]
        public void SetIfNotNull跳过空值()
        {
            JsonValue json = JsonValue.Object();
            json.SetIfNotNull("a", null);
            json.SetIfNotNull("b", JsonValue.Null);
            json.SetIfNotNull("c", JsonValue.String("x"));

            Assert.False(json.Has("a"));
            Assert.False(json.Has("b"));
            Assert.True(json.Has("c"));
        }

        [Fact]
        public void 缩进输出可读且能被重新解析()
        {
            JsonValue json = JsonValue.Object();
            json.Set("name", JsonValue.String("按钮"));
            var items = JsonValue.Array();
            items.Add(JsonValue.Bool(true));
            json.Set("items", items);

            string indented = json.ToJsonString(true);
            Assert.Contains("\n", indented);

            JsonValue parsed = JsonParser.Parse(indented);
            Assert.Equal("按钮", parsed["name"].AsString());
            Assert.True(parsed["items"][0].AsBool());
        }
    }

    public class JsonParserTests
    {
        [Fact]
        public void 解析嵌套结构()
        {
            JsonValue json = JsonParser.Parse("{\"a\":[1,2,{\"b\":\"c\"}],\"d\":null,\"e\":-1.5e2}");

            Assert.Equal(JsonKind.Object, json.Kind);
            Assert.Equal(3, json["a"].Count);
            Assert.Equal("c", json["a"][2]["b"].AsString());
            Assert.True(json["d"].IsNull);
            Assert.Equal(-150d, json["e"].AsDouble());
        }

        [Fact]
        public void 解析转义与Unicode()
        {
            JsonValue json = JsonParser.Parse("{\"s\":\"a\\u4e2d\\tb\\\"c\"}");
            Assert.Equal("a中\tb\"c", json["s"].AsString());
        }

        [Fact]
        public void 支持UTF8BOM与结尾空白()
        {
            JsonValue json = JsonParser.Parse("\uFEFF{\"a\":1}   \n");
            Assert.Equal(1, json["a"].AsInt());
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("[1,]")]
        [InlineData("{\"a\":1}extra")]
        public void 非法输入抛出解析异常(string text)
        {
            Assert.Throws<JsonParseException>(() => JsonParser.Parse(text));
        }
    }
}
