using System;
using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Pipeline;

namespace Psd2Ugui.Editor.Import
{
    /// <summary>
    /// 模块里所有身份映射文件（`.psd2ugui.json`）的集合视图。
    ///
    /// 两个用处：
    /// 1. 导出前汇总成共享贴图表，让本次导出能直接引用别的界面已经导出的图；
    /// 2. 清理失效贴图前先看看还有没有别的界面在用，免得把别人的图删了。
    /// </summary>
    public static class SharedResourceIndex
    {
        /// <summary>模块里的身份映射文件，按路径排序，保证多台机器上结果一致。</summary>
        public static List<string> ManifestFiles(ExportOptions options)
        {
            var files = new List<string>();
            string root = Psd2UguiPaths.AssetRoot(options) + "/" + Psd2UguiPaths.ManifestFolder;
            if (!Directory.Exists(root))
            {
                return files;
            }

            string[] found = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Array.Sort(found, StringComparer.Ordinal);
            for (int i = 0; i < found.Length; i++)
            {
                if (Psd2UguiManifest.IsManifestPath(found[i]))
                {
                    files.Add(found[i].Replace('\\', '/'));
                }
            }

            return files;
        }

        /// <summary>把模块里已有的贴图汇总成共享表。</summary>
        public static SharedSpriteTable Build(ExportOptions options)
        {
            List<string> files = ManifestFiles(options);
            var texts = new List<string>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    texts.Add(File.ReadAllText(files[i]));
                }
                catch (IOException)
                {
                    // 读不了就跳过，共享资源少几张不影响正确性
                }
            }

            return SharedSpriteTable.Load(texts);
        }

        /// <summary>建好共享表并挂到导出选项上，导出计划会用它解析 `ref`。</summary>
        public static SharedSpriteTable Apply(ExportOptions options)
        {
            SharedSpriteTable table = Build(options);
            options.Shared = table;
            return table;
        }

        /// <summary>哪些贴图文件还被别的界面引用着。清理失效资源时必须跳过它们。</summary>
        public static HashSet<string> InUseSpriteFiles(ExportOptions options, string exceptManifestPath)
        {
            var inUse = new HashSet<string>();
            string except = string.IsNullOrEmpty(exceptManifestPath)
                ? string.Empty
                : exceptManifestPath.Replace('\\', '/');
            List<string> files = ManifestFiles(options);
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                if (path == except)
                {
                    continue;
                }

                Psd2UguiManifest manifest = Psd2UguiManifest.Load(path);
                string spriteDir = Psd2UguiPaths.SpriteDirectory(options);
                if (!string.IsNullOrEmpty(manifest.Module))
                {
                    spriteDir = Psd2UguiPaths.AssetRoot(options) + "/" + Psd2UguiPaths.SpriteFolder + "/" +
                                manifest.Module;
                }

                foreach (Psd2UguiManifest.ManifestEntry entry in manifest.Resources.Values)
                {
                    if (!string.IsNullOrEmpty(entry.File))
                    {
                        inUse.Add(spriteDir + "/" + entry.File);
                    }
                }
            }

            return inUse;
        }
    }
}
