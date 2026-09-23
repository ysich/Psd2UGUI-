using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Json;
using Psd2Ugui.Core.Pipeline;

namespace Psd2Ugui.Editor.Import
{
    /// <summary>
    /// 落在资源目录里的身份映射文件 `.psd2ugui.json`。
    /// 记录「资源 ID → 文件 + 内容哈希 + 九宫」，重新导出时靠它判断
    /// 哪些图可以直接复用（不重写、不重导入），也为 Step 8 的增量更新留档。
    /// </summary>
    public sealed class Psd2UguiManifest
    {
        public const string FileName = ".psd2ugui.json";
        public const string CurrentVersion = "1.0.0";

        public string Version = CurrentVersion;
        public string Module = string.Empty;
        public string SourceFileName = string.Empty;

        /// <summary>资源 ID → 落盘信息。</summary>
        public readonly Dictionary<string, ManifestEntry> Resources = new Dictionary<string, ManifestEntry>();

        public sealed class ManifestEntry
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public string File = string.Empty;
            public string ContentHash = string.Empty;
            public int Width;
            public int Height;
            public UiBorder Border;
            public int SourceLayerId = -1;
            public string SourceLayerPath = string.Empty;
            public bool Shared;

            public string BorderKey
            {
                get
                {
                    return Border == null
                        ? "0,0,0,0"
                        : Border.Left + "," + Border.Bottom + "," + Border.Right + "," + Border.Top;
                }
            }
        }

        /// <summary>
        /// 上一次导出留下的、这次计划里已经没有的资源。
        /// 重新导出时用来提示（Step 8 会据此清理文件），避免目录里越攒越多。
        /// </summary>
        public List<ManifestEntry> FindObsolete(ExportPlan plan)
        {
            var obsolete = new List<ManifestEntry>();
            var keep = new HashSet<string>();
            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                keep.Add(plan.Sprites[i].ResourceId);
            }

            var ids = new List<string>(Resources.Keys);
            ids.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                ManifestEntry entry = Resources[ids[i]];
                if (!keep.Contains(entry.Id))
                {
                    obsolete.Add(entry);
                }
            }

            return obsolete;
        }

        /// <summary>资源 ID 对应的文件是否就是这份内容（内容一致就不用重写）。</summary>
        public bool IsUpToDate(SpriteExport sprite)
        {
            ManifestEntry entry;
            if (!Resources.TryGetValue(sprite.ResourceId, out entry))
            {
                return false;
            }

            return entry.ContentHash == sprite.ContentHash && entry.File == sprite.FileName &&
                   entry.BorderKey == BorderKeyOf(sprite.Border);
        }

        public static string BorderKeyOf(UiBorder border)
        {
            return border == null
                ? "0,0,0,0"
                : border.Left + "," + border.Bottom + "," + border.Right + "," + border.Top;
        }

        public string ToJsonText()
        {
            JsonValue root = JsonValue.Object()
                .Set("version", JsonValue.String(Version))
                .Set("module", JsonValue.String(Module))
                .Set("sourceFile", JsonValue.String(SourceFileName));

            JsonValue resources = JsonValue.Object();
            var ids = new List<string>(Resources.Keys);
            ids.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                ManifestEntry entry = Resources[ids[i]];
                resources.Set(entry.Id, JsonValue.Object()
                    .Set("name", JsonValue.String(entry.Name))
                    .Set("file", JsonValue.String(entry.File))
                    .Set("contentHash", JsonValue.String(entry.ContentHash))
                    .Set("width", JsonValue.Number(entry.Width))
                    .Set("height", JsonValue.Number(entry.Height))
                    .Set("border", JsonValue.String(entry.BorderKey))
                    .Set("layerId", JsonValue.Number(entry.SourceLayerId))
                    .Set("layerPath", JsonValue.String(entry.SourceLayerPath))
                    .Set("shared", JsonValue.Bool(entry.Shared)));
            }

            root.Set("resources", resources);
            return root.ToJsonString(true);
        }

        public static Psd2UguiManifest FromJsonText(string text)
        {
            var manifest = new Psd2UguiManifest();
            if (string.IsNullOrEmpty(text))
            {
                return manifest;
            }

            JsonValue root = JsonParser.Parse(text);
            manifest.Version = root["version"].AsString(CurrentVersion);
            manifest.Module = root["module"].AsString(string.Empty);
            manifest.SourceFileName = root["sourceFile"].AsString(string.Empty);

            JsonValue resources = root["resources"];
            for (int i = 0; i < resources.Count; i++)
            {
                KeyValuePair<string, JsonValue> pair = resources.Members[i];
                JsonValue item = pair.Value;
                manifest.Resources[pair.Key] = new ManifestEntry
                {
                    Id = pair.Key,
                    Name = item["name"].AsString(string.Empty),
                    File = item["file"].AsString(string.Empty),
                    ContentHash = item["contentHash"].AsString(string.Empty),
                    Width = item["width"].AsInt(),
                    Height = item["height"].AsInt(),
                    Border = ParseBorder(item["border"].AsString("0,0,0,0")),
                    SourceLayerId = item["layerId"].AsInt(-1),
                    SourceLayerPath = item["layerPath"].AsString(string.Empty),
                    Shared = item["shared"].AsBool()
                };
            }

            return manifest;
        }

        public static Psd2UguiManifest Load(string path)
        {
            return File.Exists(path) ? FromJsonText(File.ReadAllText(path)) : new Psd2UguiManifest();
        }

        public void Save(string path)
        {
            File.WriteAllText(path, ToJsonText());
        }

        public void Update(ExportPlan plan, string sourceFileName)
        {
            Module = plan.Module;
            SourceFileName = sourceFileName;
            Resources.Clear();
            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                SpriteExport sprite = plan.Sprites[i];
                Resources[sprite.ResourceId] = new ManifestEntry
                {
                    Id = sprite.ResourceId,
                    Name = sprite.Name,
                    File = sprite.FileName,
                    ContentHash = sprite.ContentHash,
                    Width = sprite.Bitmap.Width,
                    Height = sprite.Bitmap.Height,
                    Border = sprite.Border,
                    SourceLayerId = sprite.SourceLayerId,
                    SourceLayerPath = sprite.SourceLayerPath,
                    Shared = sprite.Shared
                };
            }
        }

        private static UiBorder ParseBorder(string value)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 4)
            {
                return new UiBorder(0, 0, 0, 0);
            }

            int[] numbers = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out numbers[i]))
                {
                    return new UiBorder(0, 0, 0, 0);
                }
            }

            return new UiBorder(numbers[0], numbers[1], numbers[2], numbers[3]);
        }
    }
}
