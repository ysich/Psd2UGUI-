using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Psd2Ugui.Core.Psd
{
    public enum EngineDataKind
    {
        Null = 0,
        Dict = 1,
        Array = 2,
        Text = 3,
        Number = 4,
        Bool = 5,
        Name = 6
    }

    /// <summary>Photoshop 文本引擎数据（EngineData）节点。</summary>
    public sealed class EngineDataNode
    {
        private readonly List<KeyValuePair<string, EngineDataNode>> _items =
            new List<KeyValuePair<string, EngineDataNode>>();

        private readonly List<EngineDataNode> _array = new List<EngineDataNode>();

        public EngineDataKind Kind;
        public string Text = string.Empty;
        public double Number;
        public bool Bool;

        public IList<KeyValuePair<string, EngineDataNode>> Items
        {
            get { return _items; }
        }

        public IList<EngineDataNode> Array
        {
            get { return _array; }
        }

        public bool IsDict
        {
            get { return Kind == EngineDataKind.Dict; }
        }

        public bool IsArray
        {
            get { return Kind == EngineDataKind.Array; }
        }

        public void Put(string key, EngineDataNode value)
        {
            _items.Add(new KeyValuePair<string, EngineDataNode>(key, value ?? new EngineDataNode()));
        }

        /// <summary>按键查找；键在真实文件里可能带引号，这里同时兼容。</summary>
        public EngineDataNode Get(string key)
        {
            if (Kind != EngineDataKind.Dict)
            {
                return null;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                string candidate = _items[i].Key;
                if (candidate == key || candidate == "'" + key + "'")
                {
                    return _items[i].Value;
                }
            }

            return null;
        }

        public EngineDataNode Path(params string[] keys)
        {
            EngineDataNode current = this;
            for (int i = 0; i < keys.Length && current != null; i++)
            {
                current = current.Get(keys[i]);
            }

            return current;
        }

        public EngineDataNode At(int index)
        {
            if (Kind != EngineDataKind.Array || index < 0 || index >= _array.Count)
            {
                return null;
            }

            return _array[index];
        }

        public string AsString(string fallback = "")
        {
            if (Kind == EngineDataKind.Text || Kind == EngineDataKind.Name)
            {
                return Text;
            }

            if (Kind == EngineDataKind.Number)
            {
                return Number.ToString("R", CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        public double AsNumber(double fallback = 0d)
        {
            if (Kind == EngineDataKind.Number)
            {
                return Number;
            }

            double parsed;
            if (Kind == EngineDataKind.Text &&
                double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public double GetNumber(string key, double fallback = 0d)
        {
            EngineDataNode node = Get(key);
            return node == null ? fallback : node.AsNumber(fallback);
        }

        public string GetString(string key, string fallback = "")
        {
            EngineDataNode node = Get(key);
            return node == null ? fallback : node.AsString(fallback);
        }

        public string Dump(int depth = 0, int maxDepth = 4)
        {
            var builder = new StringBuilder();
            DumpTo(builder, depth, maxDepth);
            return builder.ToString();
        }

        private void DumpTo(StringBuilder builder, int depth, int maxDepth)
        {
            switch (Kind)
            {
                case EngineDataKind.Dict:
                    builder.Append('{');
                    if (depth < maxDepth)
                    {
                        for (int i = 0; i < _items.Count; i++)
                        {
                            if (i > 0)
                            {
                                builder.Append(", ");
                            }

                            builder.Append(_items[i].Key).Append('=');
                            _items[i].Value.DumpTo(builder, depth + 1, maxDepth);
                        }
                    }

                    builder.Append('}');
                    break;
                case EngineDataKind.Array:
                    builder.Append('[');
                    if (depth < maxDepth)
                    {
                        for (int i = 0; i < _array.Count; i++)
                        {
                            if (i > 0)
                            {
                                builder.Append(", ");
                            }

                            _array[i].DumpTo(builder, depth + 1, maxDepth);
                        }
                    }

                    builder.Append(']');
                    break;
                case EngineDataKind.Text:
                    builder.Append('(').Append(Text).Append(')');
                    break;
                case EngineDataKind.Number:
                    builder.Append(Number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case EngineDataKind.Bool:
                    builder.Append(Bool ? "true" : "false");
                    break;
                case EngineDataKind.Name:
                    builder.Append('/').Append(Text);
                    break;
                default:
                    builder.Append("null");
                    break;
            }
        }
    }

    /// <summary>
    /// EngineData 解析器：Adobe 文本引擎用的紧凑结构文本格式，
    /// 形如 <c>&lt;&lt; /Key value /List [ 1 2 ] &gt;&gt;</c>。
    /// 按字节解析，因为字符串可能带 UTF-16 BOM。
    /// </summary>
    public static class EngineDataParser
    {
        public static EngineDataNode Parse(byte[] data, int offset, int length)
        {
            if (data == null || length <= 0)
            {
                return new EngineDataNode { Kind = EngineDataKind.Null };
            }

            var cursor = new Cursor(data, offset, length);
            EngineDataNode node = ParseValue(cursor, 0);
            return node ?? new EngineDataNode { Kind = EngineDataKind.Null };
        }

        private static EngineDataNode ParseValue(Cursor cursor, int depth)
        {
            if (depth > 64)
            {
                return null;
            }

            cursor.SkipWhitespace();
            if (cursor.AtEnd)
            {
                return null;
            }

            byte c = cursor.Peek();
            if (c == '<' && cursor.PeekAt(1) == '<')
            {
                return ParseDict(cursor, depth);
            }

            if (c == '[')
            {
                return ParseArray(cursor, depth);
            }

            if (c == '(')
            {
                return ParseText(cursor);
            }

            if (c == '/')
            {
                cursor.Advance();
                return new EngineDataNode { Kind = EngineDataKind.Name, Text = cursor.ReadToken() };
            }

            if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9'))
            {
                return ParseNumber(cursor);
            }

            string token = cursor.ReadToken();
            if (token == "true" || token == "false")
            {
                return new EngineDataNode { Kind = EngineDataKind.Bool, Bool = token == "true" };
            }

            if (token == "null" || token == "nil" || token.Length == 0)
            {
                return new EngineDataNode { Kind = EngineDataKind.Null };
            }

            return new EngineDataNode { Kind = EngineDataKind.Name, Text = token };
        }

        private static EngineDataNode ParseDict(Cursor cursor, int depth)
        {
            var node = new EngineDataNode { Kind = EngineDataKind.Dict };
            cursor.Advance(2);
            while (true)
            {
                cursor.SkipWhitespace();
                if (cursor.AtEnd)
                {
                    break;
                }

                if (cursor.Peek() == '>' && cursor.PeekAt(1) == '>')
                {
                    cursor.Advance(2);
                    break;
                }

                if (cursor.Peek() != '/')
                {
                    cursor.Advance();
                    continue;
                }

                cursor.Advance();
                string key = cursor.ReadToken();
                cursor.SkipWhitespace();
                // 字典里键后面一定跟值，只有紧跟字典/数组结尾时才算“没有值”。
                // 值本身可能是裸名字（形如 /Ornt /Hrzn），所以不能用 '/' 来判断。
                bool hasValue = !cursor.AtEnd && cursor.Peek() != ']' &&
                                !(cursor.Peek() == '>' && cursor.PeekAt(1) == '>');

                if (!hasValue)
                {
                    node.Put(key, new EngineDataNode { Kind = EngineDataKind.Null });
                    continue;
                }

                EngineDataNode value = ParseValue(cursor, depth + 1);
                node.Put(key, value ?? new EngineDataNode { Kind = EngineDataKind.Null });
            }

            return node;
        }

        private static EngineDataNode ParseArray(Cursor cursor, int depth)
        {
            var node = new EngineDataNode { Kind = EngineDataKind.Array };
            cursor.Advance();
            while (true)
            {
                cursor.SkipWhitespace();
                if (cursor.AtEnd)
                {
                    break;
                }

                if (cursor.Peek() == ']')
                {
                    cursor.Advance();
                    break;
                }

                EngineDataNode value = ParseValue(cursor, depth + 1);
                if (value == null)
                {
                    break;
                }

                node.Array.Add(value);
            }

            return node;
        }

        private static EngineDataNode ParseText(Cursor cursor)
        {
            cursor.Advance();
            var raw = new List<byte>();
            while (!cursor.AtEnd)
            {
                byte b = cursor.Next();
                if (b == 92 && !cursor.AtEnd)
                {
                    raw.Add(cursor.Next());
                    continue;
                }

                if (b == ')')
                {
                    break;
                }

                raw.Add(b);
            }

            return new EngineDataNode { Kind = EngineDataKind.Text, Text = DecodeText(raw) };
        }

        /// <summary>括号内的字符串按 BOM 判断编码：UTF-16BE / UTF-8 / ASCII。</summary>
        public static string DecodeText(List<byte> raw)
        {
            if (raw.Count >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
            {
                var chars = new char[(raw.Count - 2) / 2];
                for (int i = 0; i < chars.Length; i++)
                {
                    chars[i] = (char)((raw[2 + i * 2] << 8) | raw[3 + i * 2]);
                }

                return new string(chars).TrimEnd('\0');
            }

            if (raw.Count >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(raw.ToArray(), 3, raw.Count - 3);
            }

            var bytes = raw.ToArray();
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        private static EngineDataNode ParseNumber(Cursor cursor)
        {
            string token = cursor.ReadToken();
            double value;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                value = 0d;
            }

            return new EngineDataNode { Kind = EngineDataKind.Number, Number = value };
        }

        private sealed class Cursor
        {
            private readonly byte[] _data;
            private readonly int _end;
            private int _index;

            public Cursor(byte[] data, int offset, int length)
            {
                _data = data;
                _index = offset;
                _end = offset + length;
            }

            public bool AtEnd
            {
                get { return _index >= _end; }
            }

            public byte Peek()
            {
                return _index < _end ? _data[_index] : (byte)0;
            }

            public byte PeekAt(int ahead)
            {
                return _index + ahead < _end ? _data[_index + ahead] : (byte)0;
            }

            public byte Next()
            {
                return _index < _end ? _data[_index++] : (byte)0;
            }

            public void Advance(int count = 1)
            {
                _index = System.Math.Min(_end, _index + count);
            }

            public void SkipWhitespace()
            {
                while (_index < _end)
                {
                    byte b = _data[_index];
                    if (b == ' ' || b == '\r' || b == '\n' || b == '\t' || b == 0)
                    {
                        _index++;
                        continue;
                    }

                    break;
                }
            }

            public string ReadToken()
            {
                int start = _index;
                while (_index < _end && !IsDelimiter(_data[_index]))
                {
                    _index++;
                }

                if (_index == start)
                {
                    _index++;
                    return string.Empty;
                }

                return Encoding.ASCII.GetString(_data, start, _index - start);
            }

            private static bool IsDelimiter(byte b)
            {
                return b == ' ' || b == '\r' || b == '\n' || b == '\t' || b == '/' || b == ']' || b == '[' ||
                       b == '<' || b == '>' || b == ')' || b == '(' || b == 0;
            }
        }
    }
}
