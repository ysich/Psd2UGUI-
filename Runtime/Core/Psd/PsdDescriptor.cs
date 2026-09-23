using System.Collections.Generic;

namespace Psd2Ugui.Core.Psd
{
    /// <summary>Photoshop 描述符中的一个值。</summary>
    public sealed class PsdValue
    {
        public string TypeId = string.Empty;
        public double Number;
        public string Text;
        public bool Bool;
        public long Integer;
        public string Unit = string.Empty;
        public string EnumType = string.Empty;
        public string EnumValue = string.Empty;
        public string ClassId = string.Empty;
        public byte[] Data;
        public PsdDescriptor Object;
        public List<PsdValue> Items;

        public bool IsObject
        {
            get { return Object != null; }
        }

        public bool IsList
        {
            get { return Items != null; }
        }

        public double AsNumber(double fallback = 0d)
        {
            if (TypeId == "long" || TypeId == "comp")
            {
                return Integer;
            }

            if (TypeId == "bool")
            {
                return Bool ? 1d : 0d;
            }

            if (!string.IsNullOrEmpty(Text))
            {
                double parsed;
                if (double.TryParse(Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return Number;
        }

        /// <summary>把值当作颜色：'Clr ' 这类 RGBC 描述符，或直接的 0~255 分量。</summary>
        public Contract.UiColor AsColor(Contract.UiColor fallback)
        {
            if (IsObject)
            {
                return Object.ToColor(fallback);
            }

            return fallback;
        }
    }

    /// <summary>Photoshop 描述符（对象）。键顺序保留，便于诊断输出。</summary>
    public sealed class PsdDescriptor
    {
        private readonly List<KeyValuePair<string, PsdValue>> _items =
            new List<KeyValuePair<string, PsdValue>>();

        public string ClassId = string.Empty;
        public string Name = string.Empty;
        /// <summary>旧格式描述符没有 name 字段，解析时做兼容标记。</summary>
        public bool HasNameField;

        public IList<KeyValuePair<string, PsdValue>> Items
        {
            get { return _items; }
        }

        public void Add(string key, PsdValue value)
        {
            _items.Add(new KeyValuePair<string, PsdValue>(key, value ?? new PsdValue()));
        }

        public PsdValue Get(string key)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key)
                {
                    return _items[i].Value;
                }
            }

            return null;
        }

        public bool Has(string key)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key)
                {
                    return true;
                }
            }

            return false;
        }

        public PsdDescriptor GetObject(string key)
        {
            PsdValue value = Get(key);
            return value == null ? null : value.Object;
        }

        public List<PsdValue> GetList(string key)
        {
            PsdValue value = Get(key);
            return value == null ? null : value.Items;
        }

        public double GetNumber(string key, double fallback = 0d)
        {
            PsdValue value = Get(key);
            return value == null ? fallback : value.AsNumber(fallback);
        }

        public bool GetBool(string key, bool fallback = false)
        {
            PsdValue value = Get(key);
            if (value == null)
            {
                return fallback;
            }

            if (value.TypeId == "bool")
            {
                return value.Bool;
            }

            return value.AsNumber(fallback ? 1d : 0d) != 0d;
        }

        public string GetText(string key, string fallback = "")
        {
            PsdValue value = Get(key);
            if (value == null)
            {
                return fallback;
            }

            return value.Text ?? fallback;
        }

        public string GetEnum(string key, string fallback = "")
        {
            PsdValue value = Get(key);
            return value == null || string.IsNullOrEmpty(value.EnumValue) ? fallback : value.EnumValue;
        }

        public Contract.UiColor GetColor(string key, Contract.UiColor fallback)
        {
            PsdValue value = Get(key);
            return value == null ? fallback : value.AsColor(fallback);
        }

        /// <summary>
        /// 把颜色描述符转成契约颜色。PSD 里颜色分量常见两种标度：
        /// 0~1（UnFl/浮点）与 0~255（字面量），这里统一归一化到 0~1。
        /// </summary>
        public Contract.UiColor ToColor(Contract.UiColor fallback)
        {
            return Normalize(ReadRgb(this, fallback));
        }

        public static Contract.UiColor Normalize(Contract.UiColor color)
        {
            double max = color.R;
            if (color.G > max)
            {
                max = color.G;
            }

            if (color.B > max)
            {
                max = color.B;
            }

            if (max > 1.0000001d)
            {
                return new Contract.UiColor(color.R / 255d, color.G / 255d, color.B / 255d, color.A);
            }

            return color;
        }

        private static Contract.UiColor ReadRgb(PsdDescriptor descriptor, Contract.UiColor fallback)
        {
            if (descriptor == null)
            {
                return fallback;
            }

            PsdDescriptor rgb = descriptor.GetObject("Clr ");
            if (rgb == null && IsRgbClass(descriptor.ClassId))
            {
                rgb = descriptor;
            }

            if (rgb == null)
            {
                return fallback;
            }

            if (!rgb.Has("Rd  ") && !rgb.Has("Grn "))
            {
                return fallback;
            }

            double alpha = 1d;
            if (rgb.Has("alpha"))
            {
                alpha = rgb.GetNumber("alpha", 255d) / 255d;
            }

            return new Contract.UiColor(rgb.GetNumber("Rd  "), rgb.GetNumber("Grn "), rgb.GetNumber("Bl  "),
                alpha);
        }

        private static bool IsRgbClass(string classId)
        {
            return classId == "RGBC" || classId == "RGBA" || classId == "RGB ";
        }
    }

    /// <summary>
    /// 描述符读取器。
    /// 布局：name(unicode) + classID(length+key) + count + [key(length+key) + OSType + value]。
    /// 其中 length 为 0 表示后随 4 字节的已知术语（如 "null"、"Clr "）。
    /// </summary>
    public static class PsdDescriptorReader
    {
        private static readonly string[] KnownTypes =
        {
            "Objc", "GlbO", "VlLs", "doub", "UntF", "UnFl", "TEXT", "enum", "long", "comp", "bool", "type",
            "GlbC", "alis", "tdta", "ObAr", "Pth ", "obj ", "prop", "Clss", "Enmr", "rele", "Idnt"
        };

        public static PsdDescriptor ReadDescriptor(PsdBinaryReader reader)
        {
            int start = reader.Position;
            try
            {
                PsdDescriptor withName = ReadDescriptorBody(reader, true);
                if (withName != null)
                {
                    return withName;
                }
            }
            catch (PsdParseException)
            {
                // 可能是旧版格式（没有 name 字段），回退重试
            }

            reader.Seek(start);
            PsdDescriptor legacy = ReadDescriptorBody(reader, false);
            if (legacy == null)
            {
                throw new PsdParseException("描述符解析失败", start);
            }

            return legacy;
        }

        private static PsdDescriptor ReadDescriptorBody(PsdBinaryReader reader, bool withName)
        {
            var descriptor = new PsdDescriptor();
            if (withName)
            {
                int nameLength = reader.ReadInt32();
                if (nameLength < 0 || nameLength > 4096)
                {
                    return null;
                }

                descriptor.Name = nameLength == 0 ? string.Empty : reader.ReadUnicodeString(nameLength);
                descriptor.HasNameField = true;
            }

            descriptor.ClassId = ReadLengthAndKey(reader);
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000)
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                string key = ReadLengthAndKey(reader);
                PsdValue value = ReadItem(reader);
                descriptor.Add(key, value);
            }

            return descriptor;
        }

        /// <summary>读取 key / classID：长度 0 表示后随 4 字节术语。</summary>
        public static string ReadLengthAndKey(PsdBinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == 0)
            {
                return reader.ReadAscii(4);
            }

            if (length < 0 || length > reader.Remaining)
            {
                throw new PsdParseException("描述符字符串长度非法: " + length, reader.Position);
            }

            return reader.ReadAscii(length);
        }

        public static PsdValue ReadItem(PsdBinaryReader reader)
        {
            string typeId = reader.ReadSignature();
            return ReadValue(reader, typeId);
        }

        public static PsdValue ReadValue(PsdBinaryReader reader, string typeId)
        {
            var value = new PsdValue { TypeId = typeId };
            switch (typeId)
            {
                case "Objc":
                case "GlbO":
                    value.Object = ReadDescriptor(reader);
                    break;
                case "VlLs":
                case "obj ":
                    int listCount = reader.ReadInt32();
                    if (listCount < 0 || listCount > 100000)
                    {
                        throw new PsdParseException("列表长度非法: " + listCount, reader.Position);
                    }

                    var items = new List<PsdValue>(listCount);
                    for (int i = 0; i < listCount; i++)
                    {
                        items.Add(ReadItem(reader));
                    }

                    value.Items = items;
                    break;
                case "ObAr":
                    reader.ReadInt32(); // items_count
                    value.Object = ReadObjectArrayBody(reader);
                    break;
                case "doub":
                    value.Number = reader.ReadDouble();
                    break;
                case "UnFl":
                    value.Unit = reader.ReadSignature();
                    int floatCount = reader.ReadInt32();
                    if (floatCount < 0 || floatCount > 100000)
                    {
                        throw new PsdParseException("浮点数组长度非法: " + floatCount, reader.Position);
                    }

                    var floats = new List<PsdValue>(floatCount);
                    for (int i = 0; i < floatCount; i++)
                    {
                        floats.Add(new PsdValue { TypeId = "doub", Number = reader.ReadDouble() });
                    }

                    value.Items = floats;
                    if (floats.Count > 0)
                    {
                        value.Number = floats[0].Number;
                    }

                    break;
                case "UntF":
                    value.Unit = reader.ReadSignature();
                    value.Number = reader.ReadDouble();
                    break;
                case "TEXT":
                    value.Text = ReadUnicodeText(reader);
                    break;
                case "enum":
                    value.EnumType = ReadLengthAndKey(reader);
                    value.EnumValue = ReadLengthAndKey(reader);
                    break;
                case "long":
                    value.Integer = reader.ReadInt32();
                    value.Number = value.Integer;
                    break;
                case "comp":
                    value.Integer = reader.ReadInt64();
                    value.Number = value.Integer;
                    break;
                case "bool":
                    value.Bool = reader.ReadByte() != 0;
                    break;
                case "type":
                case "GlbC":
                    ReadUnicodeText(reader);
                    value.ClassId = ReadLengthAndKey(reader);
                    break;
                case "tdta":
                case "alis":
                    int dataLength = reader.ReadInt32();
                    value.Data = dataLength > 0 ? reader.ReadBytes(dataLength) : new byte[0];
                    break;
                case "Pth ":
                    value.Data = ReadPath(reader);
                    break;
                default:
                    throw new PsdParseException("不支持的描述符类型: " + typeId, reader.Position);
            }

            return value;
        }

        private static PsdDescriptor ReadObjectArrayBody(PsdBinaryReader reader)
        {
            var descriptor = ReadDescriptorBody(reader, true);
            if (descriptor == null)
            {
                throw new PsdParseException("对象数组体解析失败", reader.Position);
            }

            return descriptor;
        }

        /// <summary>TEXT 值：字符数（含结尾 null）+ UTF-16BE，无补齐。</summary>
        private static string ReadUnicodeText(PsdBinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length * 2 > reader.Remaining)
            {
                throw new PsdParseException("文本长度非法: " + length, reader.Position);
            }

            return length == 0 ? string.Empty : reader.ReadUnicodeString(length);
        }

        private static byte[] ReadPath(PsdBinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count <= 0 || count > 100000)
            {
                return new byte[0];
            }

            var data = new List<byte>();
            for (int i = 0; i < count; i++)
            {
                int selector = reader.ReadUInt16();
                switch (selector)
                {
                    case 0:
                    case 3:
                        reader.ReadUInt32();
                        reader.ReadUInt32();
                        break;
                    case 1:
                    case 2:
                        reader.ReadSignature();
                        reader.ReadUInt32();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        break;
                    case 4:
                    case 5:
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        break;
                    case 6:
                    case 7:
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        break;
                    case 8:
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        reader.ReadDouble();
                        break;
                    default:
                        throw new PsdParseException("未知路径记录: " + selector, reader.Position);
                }
            }

            return data.ToArray();
        }
    }
}
