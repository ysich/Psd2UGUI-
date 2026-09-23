using System;
using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Editor.Batch;
using UnityEditor;
using UnityEngine;

namespace Psd2Ugui.Editor
{
    /// <summary>
    /// 右键菜单与顶部菜单入口。
    ///
    /// 「选中 PSD → 一键生成」是最常用的路径，所以放在 `Assets/PSD2UGUI/` 第一位，
    /// 并且只在真的选中了 PSD 时才可用（免得点了一头雾水）。
    /// </summary>
    public static class Psd2UguiMenu
    {
        private const string Root = "Assets/PSD2UGUI/";

        [MenuItem(Root + "一键生成 Prefab %#g", false, 1)]
        public static void GeneratePrefab()
        {
            Run(SelectedPsd(), true, true);
        }

        [MenuItem(Root + "只导出资源", false, 2)]
        public static void ExportSpritesOnly()
        {
            Run(SelectedPsd(), true, false);
        }

        [MenuItem(Root + "只解析并打印结构", false, 3)]
        public static void ParseOnly()
        {
            string path = SelectedPsd();
            UiDocument document = Psd2UguiPipeline.Parse(path);
            if (document == null)
            {
                Debug.LogError("[PSD2UGUI] 解析失败：" + path);
                return;
            }

            Debug.Log("[PSD2UGUI] " + document.BuildSummary());
        }

        [MenuItem(Root + "生成所在文件夹里的所有 PSD", false, 4)]
        public static void GenerateFolder()
        {
            string path = SelectedPsd();
            if (path == null)
            {
                return;
            }

            string folder = Path.GetDirectoryName(path);
            var paths = new List<string>();
            if (Directory.Exists(folder))
            {
                string[] found = Directory.GetFiles(folder, "*.psd", SearchOption.AllDirectories);
                Array.Sort(found, StringComparer.Ordinal);
                for (int i = 0; i < found.Length; i++)
                {
                    paths.Add(found[i].Replace('\\', '/'));
                }
            }

            Psd2UguiBatch.RunAll(paths);
        }

        [MenuItem(Root + "打开导入窗口", false, 20)]
        public static void OpenWindow()
        {
            Psd2UguiWindow.ShowWindow(SelectedPsd());
        }

        // 只有选中 PSD 时，上面几项才可点
        [MenuItem(Root + "一键生成 Prefab", true)]
        [MenuItem(Root + "只导出资源", true)]
        [MenuItem(Root + "只解析并打印结构", true)]
        [MenuItem(Root + "生成所在文件夹里的所有 PSD", true)]
        public static bool ValidateSelection()
        {
            return SelectedPsd() != null;
        }

        /// <summary>当前选中的 PSD（没选中或不是 PSD 时返回 null）。</summary>
        public static string SelectedPsd()
        {
            UnityEngine.Object active = Selection.activeObject;
            if (active == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(active);
            return IsPsd(path) ? path : null;
        }

        public static bool IsPsd(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".psd" || extension == ".psb";
        }

        private static void Run(string psdPath, bool exportSprites, bool generatePrefab)
        {
            if (string.IsNullOrEmpty(psdPath))
            {
                Debug.LogError("[PSD2UGUI] 请先在 Project 里选中一个 PSD 文件。");
                return;
            }

            var options = new Psd2UguiRunOptions
            {
                PsdPath = psdPath,
                ExportSprites = exportSprites,
                GeneratePrefab = generatePrefab,
                WriteContract = true,
                WriteReport = true
            };

            Psd2UguiRunResult result = Psd2UguiPipeline.Run(options);
            Debug.Log("[PSD2UGUI] " + result.BuildSummary());
            Psd2UguiBatch.ReportProblems(result, 3);

            if (result.GeneratedSomething())
            {
                // 生成完顺手选中新预制体，方便接着拖进场景看效果
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
                if (prefab != null)
                {
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                }
            }
        }
    }
}
