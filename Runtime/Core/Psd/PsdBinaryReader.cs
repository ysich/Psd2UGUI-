using System;
using System.Text;

namespace Psd2Ugui.Core.Psd
{
    /// <summary>PSD 文件一律大端序，这里封装所有读取细节并统一做边界检查。</summary>
    public sealed class PsdBinaryReader
    {
        private readonly byte[] _data;
        private readonly int _start;
        private readonly int _end;
        private int _position;

        public PsdBinaryReader(byte[] data)
            : this(data, 0, data == null ? 0 : data.Length)
        {
        }

        public PsdBinaryReader(byte[] data, int start, int length)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            _data = data;
            _start = start;
            _end = Math.Min(data.Length, start + length);
            _position = start;
        }

        public int Position
        {
            get { return _position - _start; }
        }

        public int Length
        {
            get { return _end - _start; }
        }

        public int Remaining
        {
            get { return _end - _position; }
        }

        public bool IsAtEnd
        {
            get { return _position >= _end; }
        }

        public byte[] Buffer
        {
            get { return _data; }
        }

        public int AbsolutePosition
        {
            get { return _position; }
        }

        public void Seek(int offset)
        {
            int target = _start + offset;
            if (target < _start || target > _end)
            {
                throw new PsdParseException("定位超出范围: " + offset + "/" + Length, offset);
            }

            _position = target;
        }

        public void Skip(int count)
        {
            Seek(Position + count);
        }

        /// <summary>跳过若干字节，若超出范围则定位到末尾并返回 false。</summary>
        public bool TrySkip(int count)
        {
            int target = _position + count;
            if (count < 0 || target > _end)
            {
                _position = _end;
                return false;
            }

            _position = target;
            return true;
        }

        public byte ReadByte()
        {
            Ensure(1);
            return _data[_position++];
        }

        public byte PeekByte()
        {
            Ensure(1);
            return _data[_position];
        }

        public ushort ReadUInt16()
        {
            Ensure(2);
            ushort value = (ushort)((_data[_position] << 8) | _data[_position + 1]);
            _position += 2;
            return value;
        }

        public short ReadInt16()
        {
            return unchecked((short)ReadUInt16());
        }

        public uint ReadUInt32()
        {
            Ensure(4);
            uint value = ((uint)_data[_position] << 24) |
                         ((uint)_data[_position + 1] << 16) |
                         ((uint)_data[_position + 2] << 8) |
                         _data[_position + 3];
            _position += 4;
            return value;
        }

        public int ReadInt32()
        {
            return unchecked((int)ReadUInt32());
        }

        public ulong ReadUInt64()
        {
            ulong high = ReadUInt32();
            ulong low = ReadUInt32();
            return (high << 32) | low;
        }

        public long ReadInt64()
        {
            return unchecked((long)ReadUInt64());
        }

        public double ReadDouble()
        {
            Ensure(8);
            byte[] temp = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                // PSD 是大端，BitConverter 依赖本机序，这里手动重组
                temp[7 - i] = _data[_position + i];
            }

            _position += 8;
            return BitConverter.ToDouble(temp, 0);
        }

        public float ReadSingle()
        {
            Ensure(4);
            byte[] temp = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                temp[3 - i] = _data[_position + i];
            }

            _position += 4;
            return BitConverter.ToSingle(temp, 0);
        }

        public byte[] ReadBytes(int count)
        {
            if (count < 0)
            {
                throw new PsdParseException("读取长度为负数: " + count, Position);
            }

            Ensure(count);
            byte[] result = new byte[count];
            Array.Copy(_data, _position, result, 0, count);
            _position += count;
            return result;
        }

        /// <summary>返回原始缓冲区上的切片，不复制数据。</summary>
        public PsdBinaryReader ReadSlice(int count)
        {
            Ensure(count);
            var slice = new PsdBinaryReader(_data, _position, count);
            _position += count;
            return slice;
        }

        public string ReadSignature()
        {
            return ReadAscii(4);
        }

        public string ReadAscii(int count)
        {
            Ensure(count);
            var builder = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                builder.Append((char)_data[_position + i]);
            }

            _position += count;
            return builder.ToString();
        }

        /// <summary>Pascal 字符串：1 字节长度 + 内容，整体补齐到 padTo 的整数倍（含长度字节）。</summary>
        public string ReadPascalString(int padTo)
        {
            int length = ReadByte();
            string value = length > 0 ? ReadAscii(length) : string.Empty;
            int consumed = 1 + length;
            int remainder = consumed % padTo;
            if (remainder != 0)
            {
                Skip(padTo - remainder);
            }

            return value;
        }

        /// <summary>Unicode 字符串：UTF-16BE，charCount 为字符数（不含补齐）。</summary>
        public string ReadUnicodeString(int charCount)
        {
            if (charCount < 0)
            {
                throw new PsdParseException("Unicode 字符串长度非法: " + charCount, Position);
            }

            int byteCount = charCount * 2;
            Ensure(byteCount);
            var chars = new char[charCount];
            for (int i = 0; i < charCount; i++)
            {
                chars[i] = (char)((_data[_position + i * 2] << 8) | _data[_position + i * 2 + 1]);
            }

            _position += byteCount;
            return new string(chars).TrimEnd('\0');
        }

        /// <summary>UTF-8 字符串。</summary>
        public string ReadUtf8(int byteCount)
        {
            Ensure(byteCount);
            string value = Encoding.UTF8.GetString(_data, _position, byteCount);
            _position += byteCount;
            return value;
        }

        public void Ensure(int count)
        {
            if (_position + count > _end)
            {
                throw new PsdParseException(
                    "数据不足：需要 " + count + " 字节，剩余 " + Remaining + " 字节", Position);
            }
        }
    }

    public sealed class PsdParseException : Exception
    {
        public PsdParseException(string message, int offset)
            : base(message + " (偏移 " + offset + ")")
        {
            Offset = offset;
        }

        public int Offset { get; private set; }
    }
}
