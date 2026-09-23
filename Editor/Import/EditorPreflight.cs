using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using UnityEditor;
using UnityEngine;

namespace Psd2Ugui.Editor.Import
{
    /// <summary>
    /// 编辑器侧的预检入口。
    ///
    /// Core 的检查是纯数据的，唯独「工程里有没有这个字体」只有编辑器知道，
    /// 所以这里把 AssetDatabase 里的字体名喂给 Core，其余逻辑一律复用。
    /// </summary>
    public static class EditorPreflight
    {
        /// <summary>按导出选项跑一遍预检，返回新增的诊断条数。</summary>
        public static int Run(UiDocument document, ExportPlan plan, ExportOptions export)
        {
            return Preflight.Run(document, plan, Options(export));
        }

        public static PreflightOptions Options(ExportOptions export)
        {
            return new PreflightOptions
            {
                KnownFonts = FontNames(),
                MaxTextureSize = export == null ? 4096 : export.MaxTextureSize
            };
        }

        /// <summary>
        /// 工程里可用的字体名。
        /// 只认 uGUI 的 Font 资产；TMP 字体在可选程序集里，主程序集不引用它，
        /// 所以 TMP 项目里这条检查可能偏保守（会提示缺字体），改字体名即可。
        /// </summary>
        public static List<string> FontNames()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] guids = AssetDatabase.FindAssets("t:Font");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (font != null && seen.Add(font.name))
                {
                    names.Add(font.name);
                }
            }

            // 内置字体永远可用，别让用户看到假警报
            Add(names, seen, "Arial");
            try
            {
                Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (builtin != null)
                {
                    Add(names, seen, builtin.name);
                }
            }
            catch
            {
                // 取不到内置字体就算了，不影响其它检查
            }

            return names;
        }

        private static void Add(List<string> names, HashSet<string> seen, string name)
        {
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
            {
                names.Add(name);
            }
        }
    }
}
