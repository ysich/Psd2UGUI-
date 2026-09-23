using System;
using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Contract;
using UnityEditor;
using UnityEngine;

namespace Psd2Ugui.Editor.Batch
{
    /// <summary>
    /// 无界面批处理：CI 里一条命令把一批 PSD 全跑完。
    ///
    /// 用法（Unity 的命令行参数是自由文本，这里按固定前缀解析）：
    /// <code>
    /// Unity -batchmode -quit -projectPath &lt;项目&gt; \
    ///       -executeMethod Psd2Ugui.Editor.Batch.Psd2UguiBatch.Run \
    ///       -psd2ugui-module login \
    ///       -psd2ugui-psd Assets/UI/Login.psd \
    ///       -psd2ugui-psd Assets/UI/Hud.psd \
    ///       -psd2ugui-psdDir Assets/UI/Shop
    /// </code>
    ///
    /// 退出码：0 全部成功；1 有步骤失败（有 Error 级诊断或没产出）；2 参数不对。
    /// </summary>
    public static class Psd2UguiBatch
    {
        public const string PsdFlag = "-psd2ugui-psd";
        public const string PsdDirectoryFlag = "-psd2ugui-psdDir";
        public const string ModuleFlag = "-psd2ugui-module";
        public const string NoPrefabFlag = "-psd2ugui-no-prefab";

        /// <summary>`-executeMethod` 的入口。</summary>
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            var paths = new List<string>();
            paths.AddRange(Values(args, PsdFlag));
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == PsdDirectoryFlag)
                {
                    paths.AddRange(PsdFilesIn(args[i + 1]));
                }
            }

            for (int i = 0; i < paths.Count; i++)
            {
                paths[i] = FullPath(paths[i]);
            }

            if (paths.Count == 0)
            {
                Debug.LogError("[PSD2UGUI] 没有指定 PSD。用法：" + PsdFlag + " <路径> 或 " + PsdDirectoryFlag + " <目录>");
                EditorApplication.Exit(2);
                return;
            }

            int failures = RunAll(paths, Value(args, ModuleFlag), !Has(args, NoPrefabFlag));
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>跑一批 PSD，返回失败的个数。可以被测试或其它工具直接调用。</summary>
        public static int RunAll(IList<string> psdPaths, string module = null, bool generatePrefab = true)
        {
            int failures = 0;
            if (psdPaths == null || psdPaths.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < psdPaths.Count; i++)
            {
                var options = new Psd2UguiRunOptions { GeneratePrefab = generatePrefab };
                options.PsdPath = psdPaths[i];
                if (!string.IsNullOrEmpty(module))
                {
                    options.Export.Module = module;
                }

                Psd2UguiRunResult result = Psd2UguiPipeline.Run(options);

                // 没让生成预制体时，只要解析与导出跑完就算成功
                bool ok = result.Success && result.Errors == 0 &&
                          (generatePrefab ? result.GeneratedSomething() : result.Document != null);
                if (!ok)
                {
                    failures++;
                }

                Debug.Log("[PSD2UGUI] " + (ok ? "成功" : "失败") + " " + psdPaths[i] + " :: " +
                          result.BuildSummary());
                ReportProblems(result, 2);
            }

            Debug.Log("[PSD2UGUI] 批处理完成：" + (psdPaths.Count - failures) + "/" + psdPaths.Count +
                      " 成功" + (failures == 0 ? "。" : "，失败 " + failures + " 个。"));
            return failures;
        }

        /// <summary>把 Warning 及以上级别的诊断打到日志里，CI 日志里能直接看到原因。</summary>
        public static void ReportProblems(Psd2UguiRunResult result, int limit)
        {
            if (result == null || result.Document == null)
            {
                return;
            }

            int shown = 0;
            for (int i = 0; i < result.Document.Diagnostics.Count && shown < limit; i++)
            {
                UiDiagnostic diagnostic = result.Document.Diagnostics[i];
                if (diagnostic.Severity == DiagnosticSeverity.Info)
                {
                    continue;
                }

                shown++;
                string message = "[PSD2UGUI] " + (diagnostic.Severity == DiagnosticSeverity.Error ? "错误" : "警告") +
                                 " " + diagnostic.Code + ": " + diagnostic.Message;
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Debug.LogError(message);
                }
                else
                {
                    Debug.LogWarning(message);
                }
            }
        }

        /// <summary>把命令行里给的相对路径补成绝对路径（Unity 的工作目录就是工程根）。</summary>
        private static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(path).Replace('\\', '/');
        }

        private static IEnumerable<string> PsdFilesIn(string directory)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return files;
            }

            string[] found = Directory.GetFiles(directory, "*.psd", SearchOption.AllDirectories);
            Array.Sort(found, StringComparer.Ordinal);
            for (int i = 0; i < found.Length; i++)
            {
                files.Add(found[i].Replace('\\', '/'));
            }

            return files;
        }

        private static List<string> Values(string[] args, string flag)
        {
            var values = new List<string>();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    values.Add(args[i + 1]);
                }
            }

            return values;
        }

        private static string Value(string[] args, string flag)
        {
            List<string> values = Values(args, flag);
            return values.Count == 0 ? null : values[values.Count - 1];
        }

        private static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == flag)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
