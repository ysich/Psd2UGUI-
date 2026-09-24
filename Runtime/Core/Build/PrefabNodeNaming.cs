using System;
using System.Text;

namespace Psd2Ugui.Core.Build
{
    /// <summary>
    /// Prefab 节点名称与 UIScriptGenerator 的绑定前缀保持一致。
    /// 根对象是 Prefab 自身的名字，不参与前缀绑定；其余对象按实际挂载的主控件命名。
    /// </summary>
    public static class PrefabNodeNaming
    {
        private static readonly string[] KnownPrefixes =
        {
            "m_tmpDropdown_",
            "m_tmpInput_",
            "m_canvasGroup_",
            "m_scrollBar_",
            "m_richText_",
            "m_dropdown_",
            "m_slider_",
            "m_toggle_",
            "m_scroll_",
            "m_input_",
            "m_group_",
            "m_canvas_",
            "m_grid_",
            "m_hlay_",
            "m_vlay_",
            "m_tmp_",
            "m_text_",
            "m_rimg_",
            "m_rect_",
            "m_btn_",
            "m_img_",
            "m_go_",
            "m_tf_"
        };

        /// <summary>给计划中的所有非根节点应用绑定前缀。</summary>
        public static void Apply(PlanNode root, bool useTmpText = true)
        {
            if (root == null)
            {
                return;
            }

            for (int i = 0; i < root.Children.Count; i++)
            {
                ApplyNode(root.Children[i], useTmpText);
            }
        }

        /// <summary>按计划节点的实际组件种类生成 Unity 节点名。</summary>
        public static string NameFor(string name, ControlKind kind)
        {
            string prefix = PrefixFor(kind);
            return prefix + Suffix(name);
        }

        private static void ApplyNode(PlanNode node, bool useTmpText)
        {
            if (node == null)
            {
                return;
            }

            ControlKind namingKind = !useTmpText && node.Kind == ControlKind.TmpText
                ? ControlKind.Text
                : node.Kind;
            node.Name = NameFor(node.Name, namingKind);
            for (int i = 0; i < node.Children.Count; i++)
            {
                ApplyNode(node.Children[i], useTmpText);
            }
        }

        private static string PrefixFor(ControlKind kind)
        {
            switch (kind)
            {
                case ControlKind.Image:
                case ControlKind.Panel:
                case ControlKind.Mask:
                case ControlKind.FillColor:
                    return "m_img_";
                case ControlKind.RawImage:
                    return "m_rimg_";
                case ControlKind.Text:
                    return "m_text_";
                case ControlKind.TmpText:
                    return "m_tmp_";
                case ControlKind.Button:
                    return "m_btn_";
                case ControlKind.Toggle:
                    return "m_toggle_";
                case ControlKind.ToggleGroup:
                    return "m_group_";
                case ControlKind.Grid:
                    return "m_grid_";
                case ControlKind.Slider:
                    return "m_slider_";
                case ControlKind.Scrollbar:
                    return "m_scrollBar_";
                case ControlKind.ScrollView:
                    return "m_scroll_";
                case ControlKind.Dropdown:
                    return "m_dropdown_";
                case ControlKind.InputField:
                    return "m_input_";
                default:
                    return "m_rect_";
            }
        }

        private static string Suffix(string name)
        {
            string value = StripKnownPrefix(name);
            if (string.IsNullOrEmpty(value))
            {
                value = "Node";
            }

            var builder = new StringBuilder(value.Length);
            bool separator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsLetterOrDigit(character) || character == '_')
                {
                    if (separator && builder.Length > 0 && builder[builder.Length - 1] != '_')
                    {
                        builder.Append('_');
                    }

                    builder.Append(character);
                    separator = false;
                }
                else
                {
                    separator = true;
                }
            }

            while (builder.Length > 0 && builder[builder.Length - 1] == '_')
            {
                builder.Length--;
            }

            return builder.Length == 0 ? "Node" : builder.ToString();
        }

        private static string StripKnownPrefix(string name)
        {
            string value = (name ?? string.Empty).Trim();
            for (int i = 0; i < KnownPrefixes.Length; i++)
            {
                string prefix = KnownPrefixes[i];
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring(prefix.Length);
                }
            }

            return value;
        }
    }
}
