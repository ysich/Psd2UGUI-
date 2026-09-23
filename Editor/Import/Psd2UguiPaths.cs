using System;
using System.IO;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using UnityEditor;

namespace Psd2Ugui.Editor.Import
{
    /// <summary>
    /// 导出产物的目录约定（全部是 Assets 相对路径，用 `/` 分隔）：
    ///
    /// <code>
    /// Assets/PSD2UGUI/
    ///   sprite/&lt;module&gt;/&lt;file&gt;.png            贴图，同模块共享
    ///   manifest/&lt;module&gt;/&lt;source&gt;.psd2ugui.json 身份映射，供增量更新用
    ///   contract/&lt;module&gt;/&lt;source&gt;.json           契约 JSON，便于排查
    ///   prefab/&lt;module&gt;/&lt;source&gt;.prefab           Step 7 产出的预制体
    /// </code>
    /// </summary>
    public static class Psd2UguiPaths
    {
        public const string SpriteFolder = "sprite";
        public const string ManifestFolder = "manifest";
        public const string ContractFolder = "contract";
        public const string PrefabFolder = "prefab";

        public static string AssetRoot(ExportOptions options)
        {
            string root = options == null || string.IsNullOrEmpty(options.AssetRoot)
                ? "Assets/PSD2UGUI"
                : options.AssetRoot.TrimEnd('/');
            return root;
        }

        public static string ModuleName(ExportOptions options)
        {
            return options == null || string.IsNullOrEmpty(options.Module) ? "common" : options.Module;
        }

        /// <summary>贴图目录，对应 <see cref="UiResource.ContractPath"/> 的 `sprite/&lt;module&gt;`。</summary>
        public static string SpriteDirectory(ExportOptions options)
        {
            return AssetRoot(options) + "/" + SpriteFolder + "/" + ModuleName(options);
        }

        public static string SpritePath(ExportOptions options, string fileName)
        {
            return SpriteDirectory(options) + "/" + fileName;
        }

        public static string ManifestDirectory(ExportOptions options)
        {
            return AssetRoot(options) + "/" + ManifestFolder + "/" + ModuleName(options);
        }

        public static string ManifestPath(ExportOptions options, string sourceName)
        {
            return ManifestDirectory(options) + "/" + SafeName(sourceName) + Psd2UguiManifest.FileName;
        }

        public static string ContractDirectory(ExportOptions options)
        {
            return AssetRoot(options) + "/" + ContractFolder + "/" + ModuleName(options);
        }

        public static string ContractPath(ExportOptions options, string sourceName)
        {
            return ContractDirectory(options) + "/" + SafeName(sourceName) + ".json";
        }

        public static string PrefabDirectory(ExportOptions options)
        {
            return AssetRoot(options) + "/" + PrefabFolder + "/" + ModuleName(options);
        }

        public static string PrefabPath(ExportOptions options, string sourceName)
        {
            return PrefabDirectory(options) + "/" + SafeName(sourceName) + ".prefab";
        }

        /// <summary>把 PSD 文件名收敛成可做文件名的形式（去掉扩展名与非法字符）。</summary>
        public static string SafeName(string sourceFileName)
        {
            if (string.IsNullOrEmpty(sourceFileName))
            {
                return "psd";
            }

            string name = Path.GetFileNameWithoutExtension(sourceFileName);
            name = StableId.Sanitize(name);
            return string.IsNullOrEmpty(name) ? "psd" : name;
        }

        /// <summary>输出目录必须落在 Assets 下，否则 Unity 的资源 API 全都不认。</summary>
        public static bool IsInsideAssets(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) &&
                   (assetPath == "Assets" || assetPath.StartsWith("Assets/", StringComparison.Ordinal));
        }

        /// <summary>确保 Assets 下的目录存在，并让 AssetDatabase 立刻看到它。</summary>
        public static void EnsureAssetFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || Directory.Exists(assetFolder))
            {
                return;
            }

            Directory.CreateDirectory(assetFolder);
            AssetDatabase.Refresh();
        }
    }
}
