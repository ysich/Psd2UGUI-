using System.Globalization;
using System.Text;

namespace Psd2Ugui.Core.Contract
{
    /// <summary>
    /// 稳定身份：同一个 PSD 反复导出时，节点与资源的 ID 必须保持不变，
    /// 增量更新与资源复用全部依赖它。
    /// </summary>
    public static class StableId
    {
        public static string NodeId(string layerPath, int layerId)
        {
            return "n" + Hash(layerId.ToString(CultureInfo.InvariantCulture) + "|" + (layerPath ?? string.Empty));
        }

        public static string ResourceId(UiResourceKind kind, string key)
        {
            return "r" + Hash(kind.ToContract() + "|" + (key ?? string.Empty));
        }

        public static string FileName(string baseName, string contentHash, int width, int height)
        {
            string safe = Sanitize(baseName);
            if (string.IsNullOrEmpty(safe))
            {
                safe = "layer";
            }

            return safe + "_" + width + "x" + height + "_" + ShortHash(contentHash) + ".png";
        }

        public static string ShortHash(string value)
        {
            return Hash(value).Substring(0, 8);
        }

        public static string Hash(string value)
        {
            ulong hash = 14695981039346656037UL;
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 1099511628211UL;
                }
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static string HashBytes(byte[] data)
        {
            ulong hash = 14695981039346656037UL;
            if (data != null)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= 1099511628211UL;
                }
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>把任意名称收敛成可用于文件名的形式，中文保留。</summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool invalid = c < ' ' || c == 47 || c == 92 || c == 58 || c == 42 || c == 63 ||
                               c == 34 || c == 60 || c == 62 || c == 124 || c == 32;
                builder.Append(invalid ? '_' : c);
            }

            return builder.ToString().Trim('_');
        }
    }
}
