using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Json;

namespace Psd2Ugui.Core.Semantics
{
    /// <summary>对单个节点的人工修正。</summary>
    public sealed class NodeOverride
    {
        /// <summary>原始 key，便于诊断“这条覆盖表没生效”。</summary>
        public string Key = string.Empty;

        public bool HasType;
        public UiElementType Type;

        public bool HasRole;
        public UiRole Role;

        /// <summary>非空时覆盖节点名。</summary>
        public string Name;

        public bool Ignore;

        public bool HasNineSlice;
        public bool NineSlice;

        /// <summary>非空时把这个节点挪到指定节点下（层级修正）。</summary>
        public string Parent;

        /// <summary>非空时手动指定复用的资源名（等价于图层名写 `ref`）。</summary>
        public string Resource;

        /// <summary>这条覆盖表是否匹配给定节点。</summary>
        public bool Matches(string layerPath, int layerId, string name)
        {
            if (Key.Length == 0)
            {
                return false;
            }

            if (Key[0] == '#')
            {
                return int.TryParse(Key.Substring(1), out int id) && id == layerId;
            }

            if (Key.IndexOf('/') >= 0)
            {
                return string.Equals(Key, layerPath, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(Key, name, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 覆盖表：`{"nodes": {"&lt;路径 | #图层ID | 名字&gt;": {"type": "button", ...}}}`。
    /// 推断难免出错，覆盖表是用户纠正推断的唯一入口，也保证重新导出时改动不会丢。
    /// </summary>
    public sealed class NodeOverrides
    {
        public readonly List<NodeOverride> Entries = new List<NodeOverride>();

        /// <summary>类型写成契约名（`tmp-text`）或图层标签（`tmptxt`、`img`）都认。</summary>
        public static UiElementType ParseType(string value)
        {
            UiElementType type = UiNaming.ParseElementType(value);
            if (type != UiElementType.None)
            {
                return type;
            }

            return LayerTagParser.Parse("x." + value).Type;
        }

        /// <summary>角色写成契约名（`background`）或图层标签（`bg`）都认。</summary>
        public static UiRole ParseRole(string value)
        {
            UiRole role = UiNaming.ParseRole(value);
            if (role != UiRole.None)
            {
                return role;
            }

            return LayerTagParser.Parse("x." + value).Role;
        }

        public static NodeOverrides FromJson(JsonValue value)

        {
            var overrides = new NodeOverrides();
            if (value == null || value.IsNull)
            {
                return overrides;
            }

            JsonValue nodes = value["nodes"];
            for (int i = 0; i < nodes.Count; i++)
            {
                KeyValuePair<string, JsonValue> pair = nodes.Members[i];
                JsonValue item = pair.Value;
                var entry = new NodeOverride { Key = pair.Key };

                if (!item["type"].IsNull)
                {
                    entry.HasType = true;
                    entry.Type = ParseType(item["type"].AsString("none"));
                }

                if (!item["role"].IsNull)
                {
                    entry.HasRole = true;
                    entry.Role = ParseRole(item["role"].AsString("none"));
                }


                if (!item["name"].IsNull)
                {
                    entry.Name = item["name"].AsString(string.Empty);
                }

                entry.Ignore = item["ignore"].AsBool(false);

                if (!item["nineSlice"].IsNull)
                {
                    entry.HasNineSlice = true;
                    entry.NineSlice = item["nineSlice"].AsBool(false);
                }

                if (!item["parent"].IsNull)
                {
                    entry.Parent = item["parent"].AsString(string.Empty);
                }

                if (!item["resource"].IsNull)
                {
                    entry.Resource = item["resource"].AsString(string.Empty);
                }

                overrides.Entries.Add(entry);
            }

            return overrides;
        }

        public static NodeOverrides Parse(string jsonText)
        {
            if (string.IsNullOrEmpty(jsonText))
            {
                return new NodeOverrides();
            }

            return FromJson(JsonParser.Parse(jsonText));
        }

        /// <summary>
        /// 取出所有匹配这个节点的条目（按声明顺序）。
        /// 多条命中时后面的覆盖前面的，所以“类型”和“角色”可以分开写在两条里。
        /// </summary>
        public List<NodeOverride> MatchAll(string layerPath, int layerId, string name,
            List<int> matchedIndices = null)
        {
            List<NodeOverride> matches = null;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (!Entries[i].Matches(layerPath, layerId, name))
                {
                    continue;
                }

                matches = matches ?? new List<NodeOverride>();
                matches.Add(Entries[i]);
                if (matchedIndices != null && !matchedIndices.Contains(i))
                {
                    matchedIndices.Add(i);
                }
            }

            return matches;
        }

        /// <summary>按 key 找条目，供“改层级”等按 key 定位的场景使用。</summary>
        public NodeOverride Find(string layerPath, int layerId, string name)
        {
            List<NodeOverride> matches = MatchAll(layerPath, layerId, name);
            return matches == null ? null : matches[matches.Count - 1];
        }

    }
}
