using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Psd2Ugui.Core.Json
{
    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int position)
            : base(message + " (位置 " + position + ")")
        {
            Position = position;
        }

        public int Position { get; private set; }
    }

    /// <summary>
    /// 递归下降 JSON 解析器。用于回读契约与覆盖表，避免引入第三方依赖。
    /// </summary>
    public static class JsonParser
    {
        private const int MaxDepth = 128;

        public static JsonValue Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new JsonParseException("空文本", 0);
            }

            int index = 0;
            SkipWhitespace(text, ref index);
            JsonValue value = ParseValue(text, ref index, 0);
            SkipWhitespace(text, ref index);
            if (index != text.Length)
            {
                throw new JsonParseException("结尾存在多余字符", index);
            }

            return value;
        }

        private static JsonValue ParseValue(string text, ref int index, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new JsonParseException("嵌套过深", index);
            }

            if (index >= text.Length)
            {
                throw new JsonParseException("内容意外结束", index);
            }

            char c = text[index];
            switch (c)
            {
                case '{':
                    return ParseObject(text, ref index, depth);
                case '[':
                    return ParseArray(text, ref index, depth);
                case '"':
                    return JsonValue.String(ParseString(text, ref index));
                case 't':
                    Expect(text, ref index, "true");
                    return JsonValue.Bool(true);
                case 'f':
                    Expect(text, ref index, "false");
                    return JsonValue.Bool(false);
                case 'n':
                    Expect(text, ref index, "null");
                    return JsonValue.Null;
                default:
                    return JsonValue.Number(ParseNumber(text, ref index));
            }
        }

        private static JsonValue ParseObject(string text, ref int index, int depth)
        {
            var result = JsonValue.Object();
            index++; // '{'
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"')
                {
                    throw new JsonParseException("对象键必须是字符串", index);
                }

                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw new JsonParseException("对象缺少 ':'", index);
                }

                index++;
                SkipWhitespace(text, ref index);
                result.Set(key, ParseValue(text, ref index, depth + 1));
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new JsonParseException("对象未闭合", index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    index++;
                    return result;
                }

                throw new JsonParseException("对象中出现意外字符 '" + text[index] + "'", index);
            }
        }

        private static JsonValue ParseArray(string text, ref int index, int depth)
        {
            var result = JsonValue.Array();
            index++; // '['
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                result.Add(ParseValue(text, ref index, depth + 1));
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new JsonParseException("数组未闭合", index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == ']')
                {
                    index++;
                    return result;
                }

                throw new JsonParseException("数组中出现意外字符 '" + text[index] + "'", index);
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++; // 开引号
            var builder = new StringBuilder(32);
            while (true)
            {
                if (index >= text.Length)
                {
                    throw new JsonParseException("字符串未闭合", index);
                }

                char c = text[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    throw new JsonParseException("转义序列未完成", index);
                }

                char escape = text[index++];
                switch (escape)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            throw new JsonParseException("\\u 转义长度不足", index);
                        }

                        int code;
                        if (!int.TryParse(text.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                        {
                            throw new JsonParseException("\\u 转义不是合法十六进制", index);
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new JsonParseException("未知转义 '\\" + escape + "'", index);
                }
            }
        }

        private static double ParseNumber(string text, ref int index)
        {
            int start = index;
            if (index < text.Length && (text[index] == '-' || text[index] == '+'))
            {
                index++;
            }

            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }

            if (index < text.Length && text[index] == '.')
            {
                index++;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }
            }

            if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
            {
                index++;
                if (index < text.Length && (text[index] == '-' || text[index] == '+'))
                {
                    index++;
                }

                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }
            }

            if (index == start)
            {
                throw new JsonParseException("不是合法的 JSON 值", index);
            }

            double value;
            if (!double.TryParse(text.Substring(start, index - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value))
            {
                throw new JsonParseException("数字解析失败", start);
            }

            return value;
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length ||
                string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new JsonParseException("期望字面量 " + literal, index);
            }

            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\uFEFF')
                {
                    index++;
                    continue;
                }

                break;
            }
        }
    }
}
