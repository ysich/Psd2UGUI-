using System;
using Psd2Ugui.Core.Json;

namespace Psd2Ugui.Core.Contract
{
    /// <summary>PSD 像素坐标系下的矩形（左上角原点，Y 轴向下）。</summary>
    public struct UiRect : IEquatable<UiRect>
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public UiRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double Right
        {
            get { return X + Width; }
        }

        public double Bottom
        {
            get { return Y + Height; }
        }

        public bool IsEmpty
        {
            get { return Width <= 0d || Height <= 0d; }
        }

        public static UiRect Empty
        {
            get { return new UiRect(0d, 0d, 0d, 0d); }
        }

        public UiRect Intersect(UiRect other)
        {
            double x = Math.Max(X, other.X);
            double y = Math.Max(Y, other.Y);
            double right = Math.Min(Right, other.Right);
            double bottom = Math.Min(Bottom, other.Bottom);
            if (right <= x || bottom <= y)
            {
                return Empty;
            }

            return new UiRect(x, y, right - x, bottom - y);
        }

        public UiRect Round()
        {
            return new UiRect(Math.Round(X), Math.Round(Y), Math.Round(Width), Math.Round(Height));
        }

        public bool Equals(UiRect other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);
        }

        public override bool Equals(object obj)
        {
            return obj is UiRect && Equals((UiRect)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                hash = (hash * 397) ^ Height.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return "(" + JsonValue.FormatNumber(X) + "," + JsonValue.FormatNumber(Y) + " " +
                   JsonValue.FormatNumber(Width) + "x" + JsonValue.FormatNumber(Height) + ")";
        }
    }

    /// <summary>九宫边框（单位：像素）。</summary>
    public sealed class UiBorder : IEquatable<UiBorder>
    {
        public UiBorder(int left, int bottom, int right, int top)
        {
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
        }

        public int Left { get; private set; }

        public int Bottom { get; private set; }

        public int Right { get; private set; }

        public int Top { get; private set; }

        public bool IsZero
        {
            get { return Left == 0 && Bottom == 0 && Right == 0 && Top == 0; }
        }

        public bool Equals(UiBorder other)
        {
            return other != null && Left == other.Left && Bottom == other.Bottom && Right == other.Right && Top == other.Top;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UiBorder);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (((Left * 397) ^ Bottom) * 397 ^ Right) * 397 ^ Top;
            }
        }

        public override string ToString()
        {
            return "border(" + Left + "," + Bottom + "," + Right + "," + Top + ")";
        }
    }

    /// <summary>RGBA 颜色，分量范围 0~1。</summary>
    public struct UiColor : IEquatable<UiColor>
    {
        public double R;
        public double G;
        public double B;
        public double A;

        public UiColor(double r, double g, double b, double a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static UiColor White
        {
            get { return new UiColor(1d, 1d, 1d, 1d); }
        }

        public static UiColor Clear
        {
            get { return new UiColor(0d, 0d, 0d, 0d); }
        }

        public static UiColor FromBytes(byte r, byte g, byte b, byte a)
        {
            return new UiColor(r / 255d, g / 255d, b / 255d, a / 255d);
        }

        public UiColor WithAlpha(double alpha)
        {
            return new UiColor(R, G, B, alpha);
        }

        public string ToHexRgba()
        {
            return "#" + Channel(R) + Channel(G) + Channel(B) + Channel(A);
        }

        public bool Equals(UiColor other)
        {
            return R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
        }

        public override bool Equals(object obj)
        {
            return obj is UiColor && Equals((UiColor)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = R.GetHashCode();
                hash = (hash * 397) ^ G.GetHashCode();
                hash = (hash * 397) ^ B.GetHashCode();
                hash = (hash * 397) ^ A.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return ToHexRgba();
        }

        private static string Channel(double value)
        {
            int b = (int)Math.Round(Math.Min(1d, Math.Max(0d, value)) * 255d);
            return b.ToString("x2");
        }

        public static UiColor FromJson(JsonValue value)
        {
            if (value == null || value.IsNull)
            {
                return White;
            }

            if (value.Kind == JsonKind.String)
            {
                return FromHex(value.AsString(), White);
            }

            return new UiColor(value["r"].AsDouble(1d), value["g"].AsDouble(1d), value["b"].AsDouble(1d),
                value["a"].AsDouble(1d));
        }

        public static UiColor FromHex(string hex, UiColor fallback)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return fallback;
            }

            string text = hex.TrimStart('#');
            if (text.Length != 6 && text.Length != 8)
            {
                return fallback;
            }

            int r, g, b, a = 255;
            if (!TryParseHex(text.Substring(0, 2), out r) ||
                !TryParseHex(text.Substring(2, 2), out g) ||
                !TryParseHex(text.Substring(4, 2), out b))
            {
                return fallback;
            }

            if (text.Length == 8 && !TryParseHex(text.Substring(6, 2), out a))
            {
                return fallback;
            }

            return FromBytes((byte)r, (byte)g, (byte)b, (byte)a);
        }

        private static bool TryParseHex(string text, out int value)
        {
            return int.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
