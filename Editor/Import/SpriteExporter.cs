using System;
using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Imaging;
using Psd2Ugui.Core.Pipeline;
using UnityEditor;
using UnityEngine;

namespace Psd2Ugui.Editor.Import
{
    /// <summary>一次 Sprite 导出的结果，用于编辑器窗口与报告。</summary>
    public sealed class SpriteExportReport
    {
        public int Total;
        public int Written;
        public int Reused;
        public int Failed;
        public int Obsolete;
        public int Removed;
        public int Reimported;

        public string SpriteDirectory = string.Empty;
        public string ManifestPath = string.Empty;
        public string ContractPath = string.Empty;

        public string BuildSummary()
        {
            return "贴图 " + Total + "（新写 " + Written + "，复用 " + Reused + "，失败 " + Failed +
                   "），重设导入 " + Reimported + "，失效 " + Obsolete + "（已清理 " + Removed + "）";
        }
    }

    /// <summary>
    /// 把 Core 侧的导出计划落成 Unity 资源：
    /// 写 PNG → 设置 TextureImporter（Sprite / 九宫 / 不压缩 / 无 mipmap）→ 记录身份映射与契约 JSON。
    ///
    /// 内容哈希没变的图不重写文件，导入设置也只有不一致时才 `SaveAndReimport`，
    /// 这样反复导出不会让 Unity 反复重导全工程。
    /// </summary>
    public static class SpriteExporter
    {
        public static SpriteExportReport Export(ExportPlan plan, ExportOptions options, string sourceFileName,
            UiDocument document = null)
        {
            options = options ?? new ExportOptions();
            var report = new SpriteExportReport();
            if (plan == null)
            {
                return report;
            }

            string spriteDir = Psd2UguiPaths.SpriteDirectory(options);
            report.SpriteDirectory = spriteDir;
            report.ManifestPath = Psd2UguiPaths.ManifestPath(options, sourceFileName);
            report.ContractPath = Psd2UguiPaths.ContractPath(options, sourceFileName);
            report.Total = plan.Sprites.Count;

            if (!Psd2UguiPaths.IsInsideAssets(spriteDir) || !Psd2UguiPaths.IsInsideAssets(report.ManifestPath))
            {
                Report(document, DiagnosticSeverity.Error, "resource.outside-assets",
                    "输出根目录必须在 Assets 下（当前是 " + options.AssetRoot + "）");
                report.Failed = plan.Sprites.Count;
                return report;
            }

            Psd2UguiManifest manifest = Psd2UguiManifest.Load(report.ManifestPath);
            CleanObsolete(manifest, plan, options, document, report);

            Psd2UguiPaths.EnsureAssetFolder(spriteDir);

            var written = new List<SpriteExport>();
            var entries = new List<SpriteExport>();
            var seenFiles = new Dictionary<string, SpriteExport>();
            bool anyNewFile = false;

            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                SpriteExport sprite = plan.Sprites[i];
                if (sprite == null || sprite.Bitmap == null)
                {
                    report.Failed++;
                    Report(document, DiagnosticSeverity.Error, "resource.write-failed",
                        "资源没有位图数据，无法落盘：" + (sprite == null ? "<null>" : sprite.Name));
                    continue;
                }

                string path = Psd2UguiPaths.SpritePath(options, sprite.FileName);
                SpriteExport other;
                if (seenFiles.TryGetValue(path, out other) && other.Key != sprite.Key)
                {
                    // 理论上不会发生：文件名里已经带了内容哈希与九宫。真撞了必须报出来，否则会互相覆盖。
                    Report(document, DiagnosticSeverity.Error, "resource.file-conflict",
                        "两个不同的资源会写到同一个文件：" + path + "（" + other.Name + " / " + sprite.Name + "）");
                    report.Failed++;
                    continue;
                }

                seenFiles[path] = sprite;

                bool existed = File.Exists(path);
                bool upToDate = existed && manifest.IsUpToDate(sprite);
                if (!upToDate)
                {
                    try
                    {
                        PngEncoder.Write(path, sprite.Bitmap);
                        if (!existed)
                        {
                            anyNewFile = true;
                        }

                        report.Written++;
                        written.Add(sprite);
                    }
                    catch (Exception exception)
                    {
                        report.Failed++;
                        Report(document, DiagnosticSeverity.Error, "resource.write-failed",
                            "写贴图失败：" + path + " — " + exception.Message);
                        continue;
                    }
                }
                else
                {
                    report.Reused++;
                }

                entries.Add(sprite);

                if (sprite.Bitmap.Width > 8192 || sprite.Bitmap.Height > 8192)
                {
                    Report(document, DiagnosticSeverity.Warning, "resource.oversized",
                        "贴图超过 8192 像素，Unity 会缩放它：" + sprite.Name + " (" +
                        sprite.Bitmap.Width + "x" + sprite.Bitmap.Height + ")");
                }
            }

            if (anyNewFile || written.Count > 0)
            {
                AssetDatabase.Refresh();
            }

            for (int i = 0; i < entries.Count; i++)
            {
                SpriteExport sprite = entries[i];
                string path = Psd2UguiPaths.SpritePath(options, sprite.FileName);
                bool changed;
                if (!ApplyImportSettings(path, options, sprite, written.Contains(sprite), out changed))
                {
                    report.Failed++;
                    Report(document, DiagnosticSeverity.Warning, "resource.import-failed",
                        "拿不到贴图的导入设置（文件可能没被 Unity 收录）：" + path);
                    continue;
                }

                if (changed)
                {
                    report.Reimported++;
                }
            }

            manifest.Update(plan, sourceFileName);
            manifest.Save(report.ManifestPath);

            if (document != null)
            {
                Psd2UguiPaths.EnsureAssetFolder(Psd2UguiPaths.ContractDirectory(options));
                File.WriteAllText(report.ContractPath, ContractJson.ToJsonText(document, true));
            }

            AssetDatabase.Refresh();
            return report;
        }

        /// <summary>
        /// 设置贴图的导入参数。只有和期望值不一致时才写回并重新导入。
        /// </summary>
        /// <param name="changed">是否真的改了导入设置（改了会触发一次重导）。</param>
        /// <returns>是否拿到了导入器。</returns>
        public static bool ApplyImportSettings(string assetPath, ExportOptions options, SpriteExport sprite,
            bool justWritten, out bool changed)
        {
            changed = false;
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(assetPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            }

            if (importer == null)
            {
                return false;
            }

            int width = sprite.Bitmap.Width;
            int height = sprite.Bitmap.Height;
            UiBorder border = sprite.Border ?? new UiBorder(0, 0, 0, 0);
            var wantBorder = new Vector4(border.Left, border.Bottom, border.Right, border.Top);
            TextureImporterCompression wantCompression = options.UncompressedTextures
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.Compressed;
            int wantMaxSize = MaxTextureSizeFor(width, height, options.MaxTextureSize);

            bool dirty = justWritten ||
                         importer.textureType != TextureImporterType.Sprite ||
                         importer.spriteImportMode != SpriteImportMode.Single ||
                         importer.textureShape != TextureImporterShape.Texture2D ||
                         !Mathf.Approximately(importer.spritePixelsPerUnit, options.PixelsPerUnit) ||
                         importer.spriteBorder != wantBorder ||
                         importer.mipmapEnabled ||
                         !importer.alphaIsTransparency ||
                         importer.isReadable ||
                         importer.filterMode != FilterMode.Bilinear ||
                         importer.wrapMode != TextureWrapMode.Clamp ||
                         importer.npotScale != TextureImporterNPOTScale.None ||
                         importer.textureCompression != wantCompression ||
                         importer.maxTextureSize != wantMaxSize;

            if (!dirty)
            {
                return true;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spritePixelsPerUnit = options.PixelsPerUnit;
            importer.spriteBorder = wantBorder;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = wantCompression;
            importer.maxTextureSize = wantMaxSize;
            importer.SaveAndReimport();
            changed = true;
            return true;
        }

        /// <summary>
        /// 贴图的 Max Size 必须不小于实际边长，否则 Unity 会静默缩小贴图。
        /// 从默认 2048 逐级翻倍到够用为止，上限 8192。
        /// </summary>
        public static int MaxTextureSizeFor(int width, int height, int limit)
        {
            int longest = Math.Max(width, height);
            int size = 2048;
            int cap = limit <= 0 ? 8192 : limit;
            while (size < longest && size < cap)
            {
                size *= 2;
            }

            return Math.Min(size, 8192);
        }

        /// <summary>
        /// 清理上次导出留下、这次用不到的资源。
        ///
        /// 只删「没有任何界面还在引用」的文件：别的界面共用同一张图时，
        /// 那台界面的身份映射里还记着它，这里就会跳过。
        /// </summary>
        private static void CleanObsolete(Psd2UguiManifest manifest, ExportPlan plan, ExportOptions options,
            UiDocument document, SpriteExportReport report)
        {
            List<Psd2UguiManifest.ManifestEntry> obsolete = manifest.FindObsolete(plan);
            report.Obsolete = obsolete.Count;
            if (obsolete.Count == 0)
            {
                return;
            }

            HashSet<string> inUse = SharedResourceIndex.InUseSpriteFiles(options, report.ManifestPath);
            string spriteDir = Psd2UguiPaths.SpriteDirectory(options);
            for (int i = 0; i < obsolete.Count; i++)
            {
                Psd2UguiManifest.ManifestEntry entry = obsolete[i];
                string path = spriteDir + "/" + entry.File;
                if (inUse.Contains(path))
                {
                    Report(document, DiagnosticSeverity.Info, "resource.obsolete-shared",
                        "这张图别的界面还在用，保留：" + entry.Name + " (" + entry.File + ")");
                    continue;
                }

                if (DeleteSprite(path))
                {
                    report.Removed++;
                    Report(document, DiagnosticSeverity.Info, "resource.removed",
                        "已清理失效贴图（设计稿里已经没有了）：" + entry.Name + " (" + entry.File + ")");
                }
                else
                {
                    Report(document, DiagnosticSeverity.Warning, "resource.remove-failed",
                        "清理失效贴图失败，请手动删除：" + path);
                }
            }
        }

        private static bool DeleteSprite(string path)
        {
            if (!File.Exists(path))
            {
                // 文件已经不在（被人删了或从没落盘）：当作已清理
                AssetDatabase.Refresh();
                return true;
            }

            if (Psd2UguiPaths.IsInsideAssets(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            if (!File.Exists(path))
            {
                return true;
            }

            try
            {
                File.Delete(path);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Report(UiDocument document, DiagnosticSeverity severity, string code, string message)
        {
            if (document != null)
            {
                document.Report(severity, code, message);
            }
        }
    }
}
