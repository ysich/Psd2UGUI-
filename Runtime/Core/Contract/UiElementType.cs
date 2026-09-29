using System;

namespace Psd2Ugui.Core.Contract
{
    /// <summary>节点承载的 uGUI 元素类型。None 表示普通容器或未识别。</summary>
    public enum UiElementType
    {
        None = 0,
        Group = 1,
        Image = 2,
        RawImage = 3,
        Text = 4,
        TmpText = 5,
        FillColor = 6,
        Mask = 7,
        Panel = 8,
        Button = 9,
        TmpButton = 10,
        Toggle = 11,
        TmpToggle = 12,
        ToggleGroup = 13,
        Slider = 14,
        ScrollView = 15,
        Dropdown = 16,
        TmpDropdown = 17,
        InputField = 18,
        TmpInputField = 19,
        List = 20,
        Grid = 21,
        Ignore = 99
    }

    /// <summary>复合控件内部子节点的角色。</summary>
    public enum UiRole
    {
        None = 0,
        Background = 1,
        Highlight = 2,
        Pressed = 3,
        Selected = 4,
        Disabled = 5,
        ButtonText = 6,
        Mark = 7,
        Label = 8,
        Fill = 9,
        Handle = 10,
        Viewport = 11,
        HorizontalScrollbar = 12,
        HorizontalScrollbarBackground = 13,
        VerticalScrollbar = 14,
        VerticalScrollbarBackground = 15,
        Placeholder = 16,
        InputText = 17,
        Arrow = 18,
        Template = 19,
        Preview = 20,
        Ignore = 99
    }

    /// <summary>资源类别。</summary>
    public enum UiResourceKind
    {
        Sprite = 0,
        Texture = 1,
        Font = 2
    }

    /// <summary>角色判定：几处都按同一套名单认「交互态贴图」，规则只能有一份。</summary>
    public static class UiRoles
    {
        /// <summary>按钮的四个交互态（这四类图层会被吸收成按钮的 spriteState）。</summary>
        public static bool IsState(UiRole role)
        {
            return role == UiRole.Highlight || role == UiRole.Pressed || role == UiRole.Selected ||
                   role == UiRole.Disabled;
        }
    }

    public enum DiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>契约枚举与字符串的互转。契约里使用可读字符串，便于跨版本演进。</summary>
    public static class UiNaming
    {
        public static string ToContract(this UiElementType type)
        {
            switch (type)
            {
                case UiElementType.None:
                    return "none";
                case UiElementType.Group:
                    return "group";
                case UiElementType.Image:
                    return "image";
                case UiElementType.RawImage:
                    return "raw-image";
                case UiElementType.Text:
                    return "text";
                case UiElementType.TmpText:
                    return "tmp-text";
                case UiElementType.FillColor:
                    return "fill-color";
                case UiElementType.Mask:
                    return "mask";
                case UiElementType.Panel:
                    return "panel";
                case UiElementType.Button:
                    return "button";
                case UiElementType.TmpButton:
                    return "tmp-button";
                case UiElementType.Toggle:
                    return "toggle";
                case UiElementType.TmpToggle:
                    return "tmp-toggle";
                case UiElementType.ToggleGroup:
                    return "toggle-group";
                case UiElementType.Slider:
                    return "slider";
                case UiElementType.ScrollView:
                    return "scroll-view";
                case UiElementType.Dropdown:
                    return "dropdown";
                case UiElementType.TmpDropdown:
                    return "tmp-dropdown";
                case UiElementType.InputField:
                    return "input-field";
                case UiElementType.TmpInputField:
                    return "tmp-input-field";
                case UiElementType.List:
                    return "list";
                case UiElementType.Grid:
                    return "grid";
                case UiElementType.Ignore:
                    return "ignore";
                default:
                    return "none";
            }
        }

        public static UiElementType ParseElementType(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return UiElementType.None;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "group": return UiElementType.Group;
                case "image": return UiElementType.Image;
                case "raw-image": return UiElementType.RawImage;
                case "text": return UiElementType.Text;
                case "tmp-text": return UiElementType.TmpText;
                case "fill-color": return UiElementType.FillColor;
                case "mask": return UiElementType.Mask;
                case "panel": return UiElementType.Panel;
                case "button": return UiElementType.Button;
                case "tmp-button": return UiElementType.TmpButton;
                case "toggle": return UiElementType.Toggle;
                case "tmp-toggle": return UiElementType.TmpToggle;
                case "toggle-group": return UiElementType.ToggleGroup;
                case "slider": return UiElementType.Slider;
                case "scroll-view": return UiElementType.ScrollView;
                case "dropdown": return UiElementType.Dropdown;
                case "tmp-dropdown": return UiElementType.TmpDropdown;
                case "input-field": return UiElementType.InputField;
                case "tmp-input-field": return UiElementType.TmpInputField;
                case "list": return UiElementType.List;
                case "grid": return UiElementType.Grid;
                case "ignore": return UiElementType.Ignore;
                default: return UiElementType.None;
            }
        }

        public static string ToContract(this UiRole role)
        {
            switch (role)
            {
                case UiRole.None:
                    return "none";
                case UiRole.Background:
                    return "background";
                case UiRole.Highlight:
                    return "highlight";
                case UiRole.Pressed:
                    return "pressed";
                case UiRole.Selected:
                    return "selected";
                case UiRole.Disabled:
                    return "disabled";
                case UiRole.ButtonText:
                    return "button-text";
                case UiRole.Mark:
                    return "mark";
                case UiRole.Label:
                    return "label";
                case UiRole.Fill:
                    return "fill";
                case UiRole.Handle:
                    return "handle";
                case UiRole.Viewport:
                    return "viewport";
                case UiRole.HorizontalScrollbar:
                    return "horizontal-scrollbar";
                case UiRole.HorizontalScrollbarBackground:
                    return "horizontal-scrollbar-background";
                case UiRole.VerticalScrollbar:
                    return "vertical-scrollbar";
                case UiRole.VerticalScrollbarBackground:
                    return "vertical-scrollbar-background";
                case UiRole.Placeholder:
                    return "placeholder";
                case UiRole.InputText:
                    return "input-text";
                case UiRole.Arrow:
                    return "arrow";
                case UiRole.Template:
                    return "template";
                case UiRole.Preview:
                    return "preview";
                case UiRole.Ignore:
                    return "ignore";
                default:
                    return "none";
            }
        }

        public static UiRole ParseRole(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return UiRole.None;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "background": return UiRole.Background;
                case "highlight": return UiRole.Highlight;
                case "pressed": return UiRole.Pressed;
                case "selected": return UiRole.Selected;
                case "disabled": return UiRole.Disabled;
                case "button-text": return UiRole.ButtonText;
                case "mark": return UiRole.Mark;
                case "label": return UiRole.Label;
                case "fill": return UiRole.Fill;
                case "handle": return UiRole.Handle;
                case "viewport": return UiRole.Viewport;
                case "horizontal-scrollbar": return UiRole.HorizontalScrollbar;
                case "horizontal-scrollbar-background": return UiRole.HorizontalScrollbarBackground;
                case "vertical-scrollbar": return UiRole.VerticalScrollbar;
                case "vertical-scrollbar-background": return UiRole.VerticalScrollbarBackground;
                case "placeholder": return UiRole.Placeholder;
                case "input-text": return UiRole.InputText;
                case "arrow": return UiRole.Arrow;
                case "template": return UiRole.Template;
                case "preview": return UiRole.Preview;
                case "ignore": return UiRole.Ignore;
                default: return UiRole.None;
            }
        }

        public static string ToContract(this UiResourceKind kind)
        {
            switch (kind)
            {
                case UiResourceKind.Texture:
                    return "texture";
                case UiResourceKind.Font:
                    return "font";
                default:
                    return "sprite";
            }
        }

        public static UiResourceKind ParseResourceKind(string value)
        {
            if (string.Equals(value, "texture", StringComparison.OrdinalIgnoreCase))
            {
                return UiResourceKind.Texture;
            }

            if (string.Equals(value, "font", StringComparison.OrdinalIgnoreCase))
            {
                return UiResourceKind.Font;
            }

            return UiResourceKind.Sprite;
        }

        public static string ToContract(this DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return "error";
                case DiagnosticSeverity.Warning:
                    return "warning";
                default:
                    return "info";
            }
        }

        public static DiagnosticSeverity ParseSeverity(string value)
        {
            if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase))
            {
                return DiagnosticSeverity.Error;
            }

            if (string.Equals(value, "warning", StringComparison.OrdinalIgnoreCase))
            {
                return DiagnosticSeverity.Warning;
            }

            return DiagnosticSeverity.Info;
        }
    }
}
