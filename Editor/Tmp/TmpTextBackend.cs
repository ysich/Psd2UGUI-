using Psd2Ugui.Core.Contract;
using Psd2Ugui.Editor.Build;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Psd2Ugui.Editor.Tmp
{
    /// <summary>
    /// TextMeshPro 后端。
    ///
    /// 它放在单独的程序集里，asmdef 上带 <c>defineConstraints: PSD2UGUI_TMP</c>：
    /// 只有项目装了 com.unity.textmeshpro（由主程序集的 versionDefines 定义该宏）才会参与编译。
    /// 没装 TMP 的项目里，主程序集照常用 uGUI Text 生成，不会因为缺包编译不过。
    /// </summary>
    internal sealed class TmpTextBackend : ITmpBackend
    {
        public Graphic AddText(GameObject target, UiTextInfo info, double opacity)
        {
            var text = target.AddComponent<TextMeshProUGUI>();
            text.text = info == null ? target.name : info.Content;
            text.fontSize = (float)(info == null || info.FontSize <= 0d ? 24d : info.FontSize);
            Color color = (info == null ? UiColor.White : info.Color).ToColor();
            color.a *= (float)opacity;
            text.color = color;
            text.alignment = ToAlignment(info == null ? "center" : info.Align);
            text.enableWordWrapping = info == null || info.WordWrap;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;

            TMP_FontAsset font = FindFont(info == null ? null : info.FontName);
            if (font != null)
            {
                text.font = font;
            }

            return text;
        }

        public bool ApplyGradient(GameObject target, UiEffect effect)
        {
            var text = target.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                return false;
            }

            Color top = effect.Color.ToColor();
            Color bottom = effect.HasSecondColor ? effect.SecondColor.ToColor() : top;
            Color bottomRight = Color.Lerp(bottom, top, 0.5f);
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(top, top, bottom, bottomRight);
            return true;
        }

        private static TextAlignmentOptions ToAlignment(string align)
        {
            switch ((align ?? "center").ToLowerInvariant())
            {
                case "left":
                    return TextAlignmentOptions.Left;
                case "right":
                    return TextAlignmentOptions.Right;
                default:
                    return TextAlignmentOptions.Center;
            }
        }

        private static TMP_FontAsset FindFont(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return TMP_Settings.defaultFontAsset;
            }

            string[] guids = AssetDatabase.FindAssets("\"" + name + "\" t:TMP_FontAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null && string.Equals(font.name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }
            }

            return TMP_Settings.defaultFontAsset;
        }
    }

    /// <summary>编辑器加载时把后端注册给主程序集。</summary>
    [InitializeOnLoad]
    internal static class TmpRegistration
    {
        static TmpRegistration()
        {
            TmpBackend.Current = new TmpTextBackend();
        }
    }
}
