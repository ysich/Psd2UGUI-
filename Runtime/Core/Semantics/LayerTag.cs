using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Semantics
{
    /// <summary>
    /// 图层名里的标签：`<基础名>.<标签>.<标签>`。
    /// 与 sunsvip/PSD2UGUI_X 的约定一致：第一个点之前是节点名，之后每段都是标签，
    /// 标签可以指定控件类型（`bt`）、子节点角色（`bg`）或者九宫（`sliced`）。
    /// 前缀 `ref ` / `refp ` 表示复用已有的图片资源 / 子 Prefab。
    /// </summary>
    public sealed class LayerTag
    {
        public string RawName = string.Empty;

        /// <summary>去掉标签后的节点名。</summary>
        public string Name = string.Empty;

        /// <summary>标签指定的控件类型，None 表示没有指定。</summary>
        public UiElementType Type = UiElementType.None;

        /// <summary>标签指定的角色，None 表示没有指定。</summary>
        public UiRole Role = UiRole.None;

        /// <summary>是否标记了九宫（`sliced` / `9s` / `9`）。</summary>
        public bool NineSlice;

        /// <summary>`ref` / `refp` 指向的目标名，空表示不是引用节点。</summary>
        public string ReferenceTarget = string.Empty;

        /// <summary>true 表示 `refp`，复用的是一个子 Prefab；false 表示 `ref`，复用图片。</summary>
        public bool IsPrefabReference;

        /// <summary>是否要求整棵子树不导出。</summary>
        public bool Ignored;

        /// <summary>出现过的标签段（小写，按出现顺序）。</summary>
        public List<string> Tokens = new List<string>();

        /// <summary>不认识也不构成九宫的东西原样保留，供诊断使用。</summary>
        public List<string> UnknownTokens = new List<string>();

        public bool IsReference
        {
            get { return ReferenceTarget.Length > 0; }
        }

        public bool HasToken(string token)
        {
            for (int i = 0; i < Tokens.Count; i++)
            {
                if (Tokens[i] == token)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>标签表：把图层名里的段映射成控件类型、角色与开关。</summary>
    public static class LayerTagParser
    {
        private static readonly Dictionary<string, UiElementType> TypeTags = BuildTypeTags();
        private static readonly Dictionary<string, UiRole> RoleTags = BuildRoleTags();
        private static readonly string[] NineSliceTags = { "sliced", "slice", "9s", "9" };

        public static LayerTag Parse(string displayName)
        {
            var tag = new LayerTag { RawName = displayName ?? string.Empty };
            string name = tag.RawName.Trim();
            if (name.Length == 0)
            {
                return tag;
            }

            bool isReference = false;
            if (StartsWithWord(name, "refp"))
            {
                tag.IsPrefabReference = true;
                isReference = true;
                name = name.Substring(4).Trim();
            }
            else if (StartsWithWord(name, "ref"))
            {
                isReference = true;
                name = name.Substring(3).Trim();
            }

            int dot = name.IndexOf('.');
            if (dot < 0)
            {
                tag.Name = name;
                name = string.Empty;
            }
            else
            {
                tag.Name = name.Substring(0, dot).Trim();
                name = name.Substring(dot + 1);
            }

            if (name.Length > 0)
            {
                string[] segments = name.Split('.');
                for (int i = 0; i < segments.Length; i++)
                {
                    string token = segments[i].Trim().ToLowerInvariant();
                    if (token.Length == 0)
                    {
                        continue;
                    }

                    ApplyToken(tag, token);
                }
            }

            if (tag.Name.Length == 0)
            {
                tag.Name = tag.RawName.Trim();
            }

            if (isReference)
            {
                // `ref xxx` / `refp xxx`：基础名就是复用目标
                tag.ReferenceTarget = tag.Name;
            }

            return tag;
        }


        private static void ApplyToken(LayerTag tag, string token)
        {
            if (!tag.HasToken(token))
            {
                tag.Tokens.Add(token);
            }

            if (token == "ignore" || token == "skip")
            {
                // 忽略是开关，不是类型：整棵子树都不导出
                tag.Ignored = true;
                if (tag.Type == UiElementType.None)
                {
                    tag.Type = UiElementType.Ignore;
                }

                if (tag.Role == UiRole.None)
                {
                    tag.Role = UiRole.Ignore;
                }

                return;
            }

            UiElementType type;

            if (TypeTags.TryGetValue(token, out type))
            {
                if (tag.Type == UiElementType.None)
                {
                    tag.Type = type;
                }

                return;
            }

            UiRole role;
            if (RoleTags.TryGetValue(token, out role))
            {
                if (tag.Role == UiRole.None)
                {
                    tag.Role = role;
                }

                return;
            }

            if (IsNineSliceToken(token))
            {
                tag.NineSlice = true;
                return;
            }



            if (!tag.UnknownTokens.Contains(token))
            {
                tag.UnknownTokens.Add(token);
            }
        }

        private static bool IsNineSliceToken(string token)
        {
            for (int i = 0; i < NineSliceTags.Length; i++)
            {
                if (NineSliceTags[i] == token)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>判断字符串是否以某个单词开头，后面跟着空格。</summary>
        private static bool StartsWithWord(string value, string word)
        {
            if (value.Length <= word.Length)
            {
                return false;
            }

            if (string.Compare(value, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            return value[word.Length] == ' ';
        }

        private static Dictionary<string, UiElementType> BuildTypeTags()
        {
            var map = new Dictionary<string, UiElementType>(StringComparer.Ordinal);
            Add(map, UiElementType.Image, "img", "image", "pic", "picture", "spr", "sprite");
            Add(map, UiElementType.RawImage, "rimg", "raw", "rawimg", "rawimage", "tex", "texture");
            Add(map, UiElementType.Text, "txt", "text", "uguitext");

            Add(map, UiElementType.TmpText, "tmptxt", "tmptext", "tmp");
            Add(map, UiElementType.Button, "bt", "btn", "button");
            Add(map, UiElementType.TmpButton, "tmpbt", "tmpbtn", "tmpbutton");
            Add(map, UiElementType.Toggle, "tg", "tgl", "toggle");
            Add(map, UiElementType.TmpToggle, "tmptg", "tmptoggle");
            Add(map, UiElementType.ToggleGroup, "tgg", "togglegroup", "tgroup");
            Add(map, UiElementType.Slider, "sld", "slider", "sdr");
            Add(map, UiElementType.ScrollView, "sv", "scrollview", "scroll", "sc");
            Add(map, UiElementType.Dropdown, "dpd", "dropdown", "dd");
            Add(map, UiElementType.TmpDropdown, "tmpdpd", "tmpdropdown");
            Add(map, UiElementType.InputField, "ipt", "input", "inputfield", "edit");
            Add(map, UiElementType.TmpInputField, "tmpipt", "tmpinput", "tmpinputfield");
            Add(map, UiElementType.Mask, "msk", "mask");
            Add(map, UiElementType.FillColor, "col", "color", "fillcolor", "fillcolorlayer");
            Add(map, UiElementType.Panel, "panel", "pnl", "container");
            Add(map, UiElementType.List, "list", "lst");
            Add(map, UiElementType.Grid, "grid", "gd");
            Add(map, UiElementType.Ignore, "ignore", "skip");

            return map;
        }

        private static void Add(Dictionary<string, UiElementType> map, UiElementType type, params string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                map[tokens[i]] = type;
            }
        }

        private static Dictionary<string, UiRole> BuildRoleTags()
        {
            var map = new Dictionary<string, UiRole>(StringComparer.Ordinal);
            Add(map, UiRole.Background, "bg", "background", "back");
            Add(map, UiRole.Highlight, "onover", "over", "hover", "highlight");
            Add(map, UiRole.Pressed, "press", "pressed", "down");
            Add(map, UiRole.Selected, "select", "selected", "on", "check");
            Add(map, UiRole.Disabled, "disable", "disabled", "gray", "grey");
            Add(map, UiRole.ButtonText, "bttxt", "bttext", "buttontext");
            Add(map, UiRole.Mark, "mark", "indicator", "point");
            Add(map, UiRole.Label, "label", "lbl", "caption");
            Add(map, UiRole.Fill, "fill", "progress", "pgs");
            Add(map, UiRole.Handle, "handle", "hd", "knob", "thumb");
            Add(map, UiRole.Viewport, "vpt", "viewport", "view");
            Add(map, UiRole.HorizontalScrollbar, "hbar", "hscrollbar", "hsb");
            Add(map, UiRole.HorizontalScrollbarBackground, "hbarbg", "hscrollbarbg");
            Add(map, UiRole.VerticalScrollbar, "vbar", "vscrollbar", "vsb");
            Add(map, UiRole.VerticalScrollbarBackground, "vbarbg", "vscrollbarbg");
            Add(map, UiRole.Placeholder, "placeholder", "ph", "tips");
            Add(map, UiRole.InputText, "ipttxt", "inputtext", "iptlb", "inputlabel");
            Add(map, UiRole.Arrow, "dpdicon", "arrow", "triangle");
            Add(map, UiRole.Template, "template", "tpl", "item");
            Add(map, UiRole.Preview, "preview", "pv");
            Add(map, UiRole.Ignore, "ignore", "skip");

            return map;
        }

        private static void Add(Dictionary<string, UiRole> map, UiRole role, params string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                map[tokens[i]] = role;
            }
        }
    }
}
