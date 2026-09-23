using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Json;

namespace Psd2Ugui.Core.Pipeline
{
    /// <summary>别的界面导出过、这次可以直接拿来用的贴图（来自模块里的身份映射文件）。</summary>
    public sealed class SharedSprite
    {
        /// <summary>产出它的那次导出算出来的资源 ID。</summary>
        public string Id = string.Empty;

        /// <summary>它落在哪个模块目录下。</summary>
        public string Module = string.Empty;

        /// <summary>资源名（设计稿里的图层名，被 `ref` 用的就是它）。</summary>
        public string Name = string.Empty;

        public string FileName = string.Empty;
        public string ContentHash = string.Empty;
        public int Width;
        public int Height;
        public UiBorder Border;

        public bool IsSliceable
        {
            get { return Border != null && !Border.IsZero; }
        }
    }

    /// <summary>
    /// 共享贴图表。
    ///
    /// 界面之间复用同一张图（返回按钮、通用底框…）靠它：
    /// 引用节点先用同一个设计稿里的图，找不到才来这里按「模块 / 名字」找，
    /// 找到就当成已有资源直接挂上去，不再重新落盘。
    /// </summary>
    public sealed class SharedSpriteTable
    {
        public readonly List<SharedSprite> Sprites = new List<SharedSprite>();

        public bool IsEmpty
        {
            get { return Sprites.Count == 0; }
        }

        public void Add(SharedSprite sprite)
        {
            if (sprite != null)
            {
                Sprites.Add(sprite);
            }
        }

        /// <summary>同模块的优先，其次任意模块第一个同名的。</summary>
        public SharedSprite Find(string module, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            SharedSprite fallback = null;
            for (int i = 0; i < Sprites.Count; i++)
            {
                SharedSprite sprite = Sprites[i];
                if (sprite.Name != name || string.IsNullOrEmpty(sprite.FileName))
                {
                    continue;
                }

                if (sprite.Module == module)
                {
                    return sprite;
                }

                if (fallback == null)
                {
                    fallback = sprite;
                }
            }

            return fallback;
        }

        /// <summary>
        /// 读一批身份映射文件（`.psd2ugui.json` 的正文）。
        /// 用纯 Core 的 JSON 解析，编辑器与命令行工具走的是同一条路。
        /// </summary>
        public static SharedSpriteTable Load(IList<string> manifestTexts)
        {
            var table = new SharedSpriteTable();
            if (manifestTexts == null)
            {
                return table;
            }

            for (int i = 0; i < manifestTexts.Count; i++)
            {
                string text = manifestTexts[i];
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                JsonValue root = JsonParser.Parse(text);
                string module = root["module"].AsString(string.Empty);
                JsonValue resources = root["resources"];
                for (int j = 0; j < resources.Count; j++)
                {
                    KeyValuePair<string, JsonValue> pair = resources.Members[j];
                    JsonValue item = pair.Value;
                    table.Add(new SharedSprite
                    {
                        Id = pair.Key,
                        Module = module,
                        Name = item["name"].AsString(string.Empty),
                        FileName = item["file"].AsString(string.Empty),
                        ContentHash = item["contentHash"].AsString(string.Empty),
                        Width = item["width"].AsInt(),
                        Height = item["height"].AsInt(),
                        Border = ParseBorder(item["border"].AsString("0,0,0,0"))
                    });
                }
            }

            return table;
        }

        public static UiBorder ParseBorder(string value)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 4)
            {
                return new UiBorder(0, 0, 0, 0);
            }

            var numbers = new int[4];
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
