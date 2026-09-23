using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Psd2Ugui.Core.Json
{
    public enum JsonKind
    {
        Null = 0,
        Bool = 1,
        Number = 2,
        String = 3,
        Array = 4,
        Object = 5
    }

    /// <summary>
    /// 极简 JSON 值模型。对象成员保持插入顺序，保证契约输出可稳定比对。
    /// 不依赖第三方库，也不依赖 UnityEngine。
    /// </summary>
    public sealed class JsonValue
    {
        private static readonly JsonValue NullSingleton = new JsonValue(JsonKind.Null);
        private static readonly JsonValue TrueSingleton = new JsonValue(true);
        private static readonly JsonValue FalseSingleton = new JsonValue(false);

        private readonly JsonKind _kind;
        private readonly bool _boolValue;
        private readonly double _numberValue;
        private readonly string _stringValue;
        private readonly List<JsonValue> _items;
        private readonly List<KeyValuePair<string, JsonValue>> _members;

        private JsonValue(JsonKind kind)
        {
            _kind = kind;
            if (kind == JsonKind.Array)
            {
                _items = new List<JsonValue>();
            }
            else if (kind == JsonKind.Object)
            {
                _members = new List<KeyValuePair<string, JsonValue>>();
            }
        }

        private JsonValue(bool value)
        {
            _kind = JsonKind.Bool;
            _boolValue = value;
        }

        private JsonValue(double value)
        {
            _kind = JsonKind.Number;
            _numberValue = value;
        }

        private JsonValue(string value)
        {
            _kind = JsonKind.String;
            _stringValue = value ?? string.Empty;
        }

        public static JsonValue Null
        {
            get { return NullSingleton; }
        }

        public static JsonValue Bool(bool value)
        {
            return value ? TrueSingleton : FalseSingleton;
        }

        public static JsonValue Number(double value)
        {
            return new JsonValue(value);
        }

        public static JsonValue String(string value)
        {
            return value == null ? NullSingleton : new JsonValue(value);
        }

        public static JsonValue Array()
        {
            return new JsonValue(JsonKind.Array);
        }

        public static JsonValue Object()
        {
            return new JsonValue(JsonKind.Object);
        }

        public JsonKind Kind
        {
            get { return _kind; }
        }

        public bool IsNull
        {
            get { return _kind == JsonKind.Null; }
        }

        /// <summary>数组元素个数；非数组返回 0。</summary>
        public int Count
        {
            get
            {
                if (_kind == JsonKind.Array)
                {
                    return _items.Count;
                }

                if (_kind == JsonKind.Object)
                {
                    return _members.Count;
                }

                return 0;
            }
        }

        public IList<JsonValue> Items
        {
            get { return _items; }
        }

        public IList<KeyValuePair<string, JsonValue>> Members
        {
            get { return _members; }
        }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue found;
                return TryGet(key, out found) ? found : NullSingleton;
            }
            set { Set(key, value); }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (_kind != JsonKind.Array || index < 0 || index >= _items.Count)
                {
                    return NullSingleton;
                }

                return _items[index];
            }
        }

        /// <summary>数组追加元素。</summary>
        public JsonValue Add(JsonValue value)
        {
            RequireKind(JsonKind.Array);
            _items.Add(value ?? NullSingleton);
            return this;
        }

        /// <summary>对象设置成员；键已存在时覆盖（保持原有位置）。</summary>
        public JsonValue Set(string key, JsonValue value)
        {
            RequireKind(JsonKind.Object);
            if (key == null)
            {
                return this;
            }

            JsonValue stored = value ?? NullSingleton;
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].Key == key)
                {
                    _members[i] = new KeyValuePair<string, JsonValue>(key, stored);
                    return this;
                }
            }

            _members.Add(new KeyValuePair<string, JsonValue>(key, stored));
            return this;
        }

        /// <summary>仅在值非 null 时写入，方便可选字段。</summary>
        public JsonValue SetIfNotNull(string key, JsonValue value)
        {
            if (value == null || value.IsNull)
            {
                return this;
            }

            return Set(key, value);
        }

        public bool Has(string key)
        {
            JsonValue ignored;
            return TryGet(key, out ignored);
        }

        public bool TryGet(string key, out JsonValue value)
        {
            value = NullSingleton;
            if (_kind != JsonKind.Object || key == null)
            {
                return false;
            }

            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].Key == key)
                {
                    value = _members[i].Value;
                    return true;
                }
            }

            return false;
        }

        public string AsString(string fallback = null)
        {
            if (_kind == JsonKind.String)
            {
                return _stringValue;
            }

            if (_kind == JsonKind.Number)
            {
                return FormatNumber(_numberValue);
            }

            if (_kind == JsonKind.Bool)
            {
                return _boolValue ? "true" : "false";
            }

            return fallback;
        }

        public double AsDouble(double fallback = 0d)
        {
            if (_kind == JsonKind.Number)
            {
                return _numberValue;
            }

            if (_kind == JsonKind.String)
            {
                double parsed;
                if (double.TryParse(_stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            if (_kind == JsonKind.Bool)
            {
                return _boolValue ? 1d : 0d;
            }

            return fallback;
        }

        public int AsInt(int fallback = 0)
        {
            if (_kind == JsonKind.Null)
            {
                return fallback;
            }

            return (int)Math.Round(AsDouble(fallback));
        }

        public float AsFloat(float fallback = 0f)
        {
            if (_kind == JsonKind.Null)
            {
                return fallback;
            }

            return (float)AsDouble(fallback);
        }

        public bool AsBool(bool fallback = false)
        {
            if (_kind == JsonKind.Bool)
            {
                return _boolValue;
            }

            if (_kind == JsonKind.Number)
            {
                return Math.Abs(_numberValue) > double.Epsilon;
            }

            if (_kind == JsonKind.String)
            {
                return string.Equals(_stringValue, "true", StringComparison.OrdinalIgnoreCase);
            }

            return fallback;
        }

        public string ToJsonString(bool indented = true)
        {
            var builder = new StringBuilder(256);
            Write(builder, indented, 0);
            return builder.ToString();
        }

        public override string ToString()
        {
            return ToJsonString(false);
        }

        private void RequireKind(JsonKind kind)
        {
            if (_kind != kind)
            {
                throw new InvalidOperationException(
                    "JsonValue kind is " + _kind + ", expected " + kind + ".");
            }
        }

        private void Write(StringBuilder builder, bool indented, int depth)
        {
            switch (_kind)
            {
                case JsonKind.Null:
                    builder.Append("null");
                    break;
                case JsonKind.Bool:
                    builder.Append(_boolValue ? "true" : "false");
                    break;
                case JsonKind.Number:
                    builder.Append(FormatNumber(_numberValue));
                    break;
                case JsonKind.String:
                    WriteString(builder, _stringValue);
                    break;
                case JsonKind.Array:
                    WriteArray(builder, indented, depth);
                    break;
                case JsonKind.Object:
                    WriteObject(builder, indented, depth);
                    break;
            }
        }

        private void WriteArray(StringBuilder builder, bool indented, int depth)
        {
            if (_items.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteNewLine(builder, indented, depth + 1);
                _items[i].Write(builder, indented, depth + 1);
            }

            WriteNewLine(builder, indented, depth);
            builder.Append(']');
        }

        private void WriteObject(StringBuilder builder, bool indented, int depth)
        {
            if (_members.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');
            for (int i = 0; i < _members.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteNewLine(builder, indented, depth + 1);
                WriteString(builder, _members[i].Key);
                builder.Append(':');
                if (indented)
                {
                    builder.Append(' ');
                }

                _members[i].Value.Write(builder, indented, depth + 1);
            }

            WriteNewLine(builder, indented, depth);
            builder.Append('}');
        }

        private static void WriteNewLine(StringBuilder builder, bool indented, int depth)
        {
            if (!indented)
            {
                return;
            }

            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "0";
            }

            if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
            {
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
