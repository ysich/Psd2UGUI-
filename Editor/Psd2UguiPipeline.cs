using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Json;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Psd2Ugui.Editor.Build;
using Psd2Ugui.Editor.Import;
using UnityEditor;
using UnityEngine;
using BuildOptions = Psd2Ugui.Core.Build.BuildOptions;
using Debug = UnityEngine.Debug;

namespace Psd2Ugui.Editor
{
    /// <summary>一次导入的完整参数。窗口、菜单、批处理都走这一份，保证三处行为一致。</summary>
    public sealed class Psd2UguiRunOptions
    {
        /// <summary>PSD 路径（Assets 相对路径最好，绝对路径也认）。</summary>
        public string PsdPath;

        public ExportOptions Export = new ExportOptions();
        public BuildOptions Build = new BuildOptions();
        public NodeBuildOptions Semantics = new NodeBuildOptions();

        /// <summary>落一份身份映射与契约 JSON（增量更新靠它）。</summary>
        public bool WriteContract = true;

        /// <summary>写 PNG 并设置导入参数。</summary>
        public bool ExportSprites = true;

        /// <summary>生成/更新预制体。</summary>
        public bool GeneratePrefab = true;

        /// <summary>跑预检（含「工程里有没有这个字体」）。</summary>
        public bool RunPreflight = true;

        /// <summary>写体检报告。</summary>
        public bool WriteReport = true;

        /// <summary>本文件里没有的 `ref` 去别的界面导出的图里找。</summary>
        public bool ReuseSharedResources = true;

        /// <summary>工程里已经有同名预制体时走增量更新，而不是整棵重建。</summary>
        public bool IncrementalPrefab = true;
    }

    /// <summary>一次导入的结果：文档、计划、各步报告，以及最终的预制体。</summary>
    public sealed class Psd2UguiRunResult
    {
        public string PsdPath = string.Empty;
        public string Module = string.Empty;
        public bool Success;

        public UiDocument Document;
        public ExportPlan ExportPlan;
        public SpriteExportReport Sprites;
        public PrefabProduct Prefab;
        public ImportReport Report;

        public string ContractPath = string.Empty;
        public string ReportPath = string.Empty;
        public string PrefabPath = string.Empty;

        public double ParseMilliseconds;
        public double TotalMilliseconds;

        public int Errors
        {
            get { return Document == null ? 0 : Document.CountSeverity(DiagnosticSeverity.Error); }
        }

        public int Warnings
        {
            get { return Document == null ? 0 : Document.CountSeverity(DiagnosticSeverity.Warning); }
        }

        public int Infos
        {
            get { return Document == null ? 0 : Document.CountSeverity(DiagnosticSeverity.Info); }
        }

        /// <summary>这次跑出来东西了没有（批处理判定成功用）。</summary>
        public bool GeneratedSomething()
        {
            return Prefab != null && Prefab.Build.Prefab != null;
        }

        public string BuildSummary()
        {
            if (Document == null)
            {
                return "没有产出（" + PsdPath + "）";
            }

            var parts = new List<string>();
            parts.Add(Document.Stats.Count > 0 && Document.Stats.ContainsKey("nodes")
                ? Document.Stats["nodes"] + " 个节点"
                : "0 个节点");
            if (ExportPlan != null)
            {
                parts.Add(ExportPlan.Sprites.Count + " 张图（可切 " + ExportPlan.SliceableCount + "，复用 " +
                          (ExportPlan.Reused.Count + (Document.Stats.ContainsKey("spriteShared")
                              ? Document.Stats["spriteShared"]
                              : "0")) + "）");
            }

            if (Prefab != null)
            {
                parts.Add(Prefab.BuildSummary());
            }

            parts.Add("诊断 " + (Errors + Warnings + Infos) + " (E" + Errors + "/W" + Warnings + "/I" + Infos + ")");
            parts.Add("耗时 " + TotalMilliseconds.ToString("0.#") + " ms");
            return string.Join("，", parts.ToArray());
        }
    }

    /// <summary>
    /// 一条命令跑完「PSD → 资源 → 预制体 → 报告」。
    ///
    /// 之所以要这么一个编排层，是为了让编辑器窗口、右键菜单和 CI 批处理跑的是同一段逻辑：
    /// 任何一处改了参数或顺序，另外两处自动跟上，不会出现「窗口能跑、命令行跑不出同样结果」。
    /// </summary>
    public static class Psd2UguiPipeline
    {
        public static Psd2UguiRunResult Run(Psd2UguiRunOptions options)
        {
            var result = new Psd2UguiRunResult();
            options = options ?? new Psd2UguiRunOptions();
            result.PsdPath = options.PsdPath ?? string.Empty;
            var watch = Stopwatch.StartNew();

            if (string.IsNullOrEmpty(options.PsdPath) || !File.Exists(options.PsdPath))
            {
                Debug.LogError("[PSD2UGUI] 找不到 PSD 文件：" + options.PsdPath);
                result.TotalMilliseconds = watch.Elapsed.TotalMilliseconds;
                return result;
            }

            string sourceName = Path.GetFileName(options.PsdPath);

            // 1. 解析二进制
            var parseWatch = Stopwatch.StartNew();
            PsdFile file = PsdParser.Read(File.ReadAllBytes(options.PsdPath), sourceName);
            parseWatch.Stop();
            result.ParseMilliseconds = parseWatch.Elapsed.TotalMilliseconds;

            // 2. 语义：图层树 → 契约节点树（人工覆盖表优先）
            LoadOverrides(options, sourceName);
            UiDocument document = NodeBuilder.Build(file, options.Semantics);
            document.Document.SourcePath = options.PsdPath;
            result.Document = document;

            // 3. 资源计划（含跨界面复用）
            var planWatch = Stopwatch.StartNew();
            if (options.ReuseSharedResources)
            {
                SharedResourceIndex.Apply(options.Export);
            }

            ExportPlan exportPlan = ExportPlanner.Build(file, document, options.Export);
            result.ExportPlan = exportPlan;
            result.Module = exportPlan.Module;

            // 计划跑完才知道模块名，回填给路径计算，避免产物落到别的模块目录
            options.Export.Module = exportPlan.Module;

            // 4. 预检：先把「用户需要知道的事」说出来，再动手写文件
            if (options.RunPreflight)
            {
                EditorPreflight.Run(document, exportPlan, options.Export);
            }

            // 5. 落盘贴图与契约
            if (options.ExportSprites)
            {
                result.Sprites = SpriteExporter.Export(exportPlan, options.Export, sourceName, document);
            }

            if (options.WriteContract)
            {
                result.ContractPath = Psd2UguiPaths.ContractPath(options.Export, sourceName);
                Psd2UguiPaths.EnsureAssetFolder(Psd2UguiPaths.ContractDirectory(options.Export));
                File.WriteAllText(result.ContractPath, ContractJson.ToJsonText(document, true));
                AssetDatabase.Refresh();
            }

            // 6. 装配预制体
            if (options.GeneratePrefab)
            {
                options.Build.UseTmpText = TmpBackend.Available;
                PlanNode plan = PrefabPlanner.Build(document, options.Build);
                if (plan == null)
                {
                    document.Report(DiagnosticSeverity.Error, "prefab.empty-plan",
                        "装配计划为空，没有生成预制体：" + sourceName);
                }
                else
                {
                    var context = new PrefabBuildContext
                    {
                        Document = document,
                        Build = options.Build,
                        Export = options.Export,
                        SourcePsd = sourceName
                    };

                    string prefabPath = Psd2UguiPaths.PrefabPath(options.Export, sourceName);
                    result.Prefab = options.IncrementalPrefab
                        ? IncrementalBuilder.Build(document, plan, context, prefabPath)
                        : FreshBuild(document, plan, context, prefabPath);
                    result.PrefabPath = prefabPath;
                }
            }

            planWatch.Stop();

            // 7. 体检报告
            if (options.WriteReport)
            {
                ImportReport report = ImportReport.From(document, exportPlan);
                report.SourceBytes = new FileInfo(options.PsdPath).Length;
                report.ParseMilliseconds = result.ParseMilliseconds;
                report.PlanMilliseconds = planWatch.Elapsed.TotalMilliseconds;
                result.Report = report;
                result.ReportPath = Psd2UguiPaths.ReportPath(options.Export, sourceName);
                Psd2UguiPaths.EnsureAssetFolder(Psd2UguiPaths.ReportDirectory(options.Export));
                File.WriteAllText(result.ReportPath, report.ToJsonText(true));
                AssetDatabase.Refresh();
            }

            result.TotalMilliseconds = watch.Elapsed.TotalMilliseconds;
            result.Success = !document.HasErrors;
            return result;
        }

        /// <summary>最省事的入口：给一个 PSD 路径，其余全用默认值。</summary>
        public static Psd2UguiRunResult Run(string psdPath, string module = null, bool generatePrefab = true)
        {
            var options = new Psd2UguiRunOptions { GeneratePrefab = generatePrefab };
            options.PsdPath = psdPath;
            if (!string.IsNullOrEmpty(module))
            {
                options.Export.Module = module;
            }

            return Run(options);
        }

        /// <summary>覆盖表放在 `<AssetRoot>/overrides/<模块>/`，和产物挨着，方便随包提交。</summary>
        public static NodeOverrides LoadOverrides(Psd2UguiRunOptions options, string sourceName)
        {
            string path = Psd2UguiPaths.OverridePath(options.Export, sourceName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                options.Semantics.Overrides = NodeOverrides.Parse(File.ReadAllText(path));
                return options.Semantics.Overrides;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PSD2UGUI] 覆盖表读不了，已按空表继续：" + path + " — " + exception.Message);
                return null;
            }
        }

        /// <summary>把覆盖表写回磁盘（编辑器里改类型/角色后调用）。</summary>
        public static void SaveOverrides(ExportOptions export, string sourceName, NodeOverrides overrides)
        {
            string path = Psd2UguiPaths.OverridePath(export, sourceName);
            Psd2UguiPaths.EnsureAssetFolder(Psd2UguiPaths.OverrideDirectory(export));
            File.WriteAllText(path, (overrides ?? new NodeOverrides()).ToJsonText());
            AssetDatabase.Refresh();
        }

        private static PrefabProduct FreshBuild(UiDocument document, PlanNode plan, PrefabBuildContext context,
            string prefabPath)
        {
            var product = new PrefabProduct();
            PrefabBuildResult built = PrefabBuilder.BuildPrefab(document, plan, context, prefabPath);
            product.Build.Prefab = built.Prefab;
            product.Build.PrefabPath = built.PrefabPath;
            product.Build.Nodes = built.Nodes;
            product.Created = built.Nodes;
            return product;
        }

        /// <summary>解析 PSD 但不落任何产物，给编辑器窗口的「解析」按钮用。</summary>
        public static UiDocument Parse(string psdPath, NodeBuildOptions semantics = null)
        {
            if (string.IsNullOrEmpty(psdPath) || !File.Exists(psdPath))
            {
                return null;
            }

            PsdFile file = PsdParser.Read(File.ReadAllBytes(psdPath), Path.GetFileName(psdPath));
            UiDocument document = NodeBuilder.Build(file, semantics);
            document.Document.SourcePath = psdPath;
            return document;
        }
    }
}
