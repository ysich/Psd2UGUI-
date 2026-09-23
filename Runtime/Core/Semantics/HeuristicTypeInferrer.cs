using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Semantics
{
    /// <summary>
    /// 兜底推断：图层类型 + 名称关键词。
    /// 只在标签与覆盖表都没给出类型时介入，所以规则可以写得“贪心”一点，
    /// 判错了用户用覆盖表改就行。
    /// </summary>
    public sealed class HeuristicTypeInferrer : ITypeInferrer
    {
        private readonly UiElementType _textType;
        private readonly UiElementType _groupType;
        private readonly KeyValuePair<string, UiElementType>[] _keywords;

        public HeuristicTypeInferrer(UiElementType textType = UiElementType.TmpText,
            UiElementType groupType = UiElementType.Group)
        {
            _textType = textType;
            _groupType = groupType;
            _keywords = BuildKeywords();
        }


        public string Name
        {
            get { return "heuristic"; }
        }

        public UiElementType Infer(InferenceContext context)
        {
            if (context.IsReference && !context.IsGroup)
            {
                // 复用已有图片：类型由标签决定，没写标签就按图片处理
                return UiElementType.Image;
            }


            if (context.HasText)
            {
                return _textType;
            }

            if (context.IsGroup)
            {
                return _groupType;
            }

            if (context.HasSolidFill)
            {
                return UiElementType.FillColor;
            }

            // 写了角色标签说明它是某个复合控件的一部分，别抢着按名字认成控件
            if (context.TaggedRole != UiRole.None)
            {
                return context.HasPixelData ? UiElementType.Image : UiElementType.None;
            }

            string name = (context.Tag == null ? string.Empty : context.Tag.Name).ToLowerInvariant();
            if (name.Length > 0)
            {
                for (int i = 0; i < _keywords.Length; i++)
                {
                    if (name.IndexOf(_keywords[i].Key, StringComparison.Ordinal) >= 0)
                    {
                        return _keywords[i].Value;
                    }
                }

            }

            return context.HasPixelData ? UiElementType.Image : UiElementType.None;
        }

        /// <summary>关键词按优先级排列，越靠前越先命中。</summary>
        private KeyValuePair<string, UiElementType>[] BuildKeywords()
        {

            // 复合控件的具体类型跟着文本方案走：TMP 方案给 Tmp* 变体，UGUI Text 方案给基础变体
            bool tmp = _textType != UiElementType.Text;

            var list = new List<KeyValuePair<string, UiElementType>>();
            Add(list, tmp ? UiElementType.TmpButton : UiElementType.Button, "button", "btn", "按钮");
            Add(list, tmp ? UiElementType.TmpToggle : UiElementType.Toggle, "toggle", "checkbox", "开关", "勾选");
            Add(list, UiElementType.Slider, "slider", "滑动");
            Add(list, UiElementType.ScrollView, "scrollview", "scroll", "滚动");
            Add(list, tmp ? UiElementType.TmpDropdown : UiElementType.Dropdown, "dropdown", "combo", "下拉");
            Add(list, tmp ? UiElementType.TmpInputField : UiElementType.InputField, "inputfield", "input", "editbox", "输入框");

            Add(list, UiElementType.Mask, "mask", "遮罩");
            Add(list, UiElementType.List, "list", "列表");
            Add(list, UiElementType.Grid, "grid");
            Add(list, UiElementType.Panel, "panel", "window", "container", "面板", "窗口");
            Add(list, UiElementType.FillColor, "fillcolor", "colorfill", "纯色");
            Add(list, UiElementType.Image, "icon", "image", "img", "picture", "logo", "bg", "background",
                "背景", "图标", "图片");
            Add(list, UiElementType.Text, "text", "title", "label", "txt", "标题", "文字");
            return list.ToArray();
        }

        private static void Add(List<KeyValuePair<string, UiElementType>> list, UiElementType type,
            params string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                list.Add(new KeyValuePair<string, UiElementType>(keywords[i], type));
            }
        }
    }
}
