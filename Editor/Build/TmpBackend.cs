using Psd2Ugui.Core.Contract;
using UnityEngine;
using UnityEngine.UI;

namespace Psd2Ugui.Editor.Build
{
    /// <summary>
    /// TMP 的接入点。TextMeshPro 是可选包，主程序集不直接引用它：
    /// 装了 TMP 的项目里，<c>Psd2Ugui.Editor.Tmp</c>（带 defineConstraints 的可选程序集）
    /// 会在加载时把实现注册进来；没装就退回 uGUI Text 并给出诊断。
    /// </summary>
    public interface ITmpBackend
    {
        /// <summary>给对象加 TMP 文本组件并套用样式，返回的组件一定是 Graphic。</summary>
        Graphic AddText(GameObject target, UiTextInfo text, double opacity);

        /// <summary>把渐变填充套成 TMP 的顶点渐变；做不了就返回 false。</summary>
        bool ApplyGradient(GameObject target, UiEffect effect);
    }

    /// <summary>TMP 后端的注册表。</summary>
    public static class TmpBackend
    {
        /// <summary>由 TMP 程序集在加载时赋值。</summary>
        public static ITmpBackend Current;

        public static bool Available
        {
            get { return Current != null; }
        }
    }
}
