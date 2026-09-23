using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Imaging;
using Psd2Ugui.Core.Psd;

namespace Psd2Ugui.Core.Pipeline
{
    /// <summary>
    /// 把契约节点树翻译成“要落盘的资源清单”：
    /// 栅格化 → 去空 → 九宫检测 → 内容去重 → 绑定资源 ID → 解析 `ref` 引用。
    ///
    /// 这一步不碰 Unity：给 Unity 的是 PNG 字节（由 Editor 侧写盘并设置导入参数），
    /// 因此可以脱离编辑器用 dotnet 测试跑通。
    /// </summary>
    public static class ExportPlanner
    {
        public static ExportPlan Build(PsdFile file, UiDocument document, ExportOptions options = null)
        {
            var plan = new ExportPlan();
            if (file == null || document == null)
            {
                return plan;
            }

            options = options ?? new ExportOptions();
            string module = ModuleOf(document, options);
            plan.AssetRoot = options.AssetRoot;
            plan.Module = module;
            document.Document.Module = module;

            var warnings = new List<string>();
            var byContent = new Dictionary<string, SpriteExport>();
            var byName = new Dictionary<string, List<SpriteExport>>();
            var references = new List<UiNode>();

            foreach (UiNode node in document.Nodes())
            {
                if (!NeedsSprite(node))
                {
                    continue;
                }

                if (node.IsPrefabReference)
                {
                    document.Report(DiagnosticSeverity.Info, "resource.prefab-reference",
                        "refp 节点按子 Prefab 处理，不导出贴图：" + node.Name, node);
                    continue;
                }

                if (!string.IsNullOrEmpty(node.ReferenceTarget))
                {
                    references.Add(node);
                    continue;
                }

                if (!node.Visible && !options.IncludeHiddenLayers)
                {
                    continue;
                }

                PsdLayer layer = file.FindLayer(node.LayerId);
                if (layer == null)
                {
                    document.Report(DiagnosticSeverity.Warning, "resource.layer-missing",
                        "找不到图层，跳过资源导出：" + node.Name + " (#" + node.LayerId + ")", node);
                    continue;
                }

                Bitmap bitmap = LayerRasterizer.Rasterize(file, layer, warnings);
                if (bitmap == null || bitmap.IsEmpty || bitmap.IsFullyTransparent())
                {
                    document.Report(DiagnosticSeverity.Info, "resource.empty",
                        "图层没有可导出的像素：" + node.Name, node);
                    continue;
                }

                NineSliceResult nine = options.DetectNineSlice
                    ? NineSliceDetector.Detect(bitmap, options.NineSlice)
                    : Plain(bitmap);
                if (nine.Sprite == null)
                {
                    document.Report(DiagnosticSeverity.Info, "resource.empty",
                        "图层去空之后没有内容：" + node.Name + " (" + nine.Reason + ")", node);
                    continue;
                }

                bool hinted = node.Tags.ContainsKey("nine-slice");
                if (hinted && !nine.IsSliceable)
                {
                    document.Report(DiagnosticSeverity.Warning, "nine-slice.failed",
                        "图层标了 sliced 但没有检测出可用的九宫：" + node.Name + " (" + nine.Reason + ")", node);
                }

                string hash = StableId.HashBytes(nine.Sprite.Pixels);
                var candidate = new SpriteExport
                {
                    Name = string.IsNullOrEmpty(node.Name) ? "sprite" : node.Name,
                    Bitmap = nine.Sprite,
                    Border = nine.Border,
                    ContentHash = hash,
                    SourceLayerId = node.LayerId,
                    SourceLayerPath = node.LayerPath,
                    SourceRect = nine.SourceRect,
                    ResourceId = StableId.ResourceId(UiResourceKind.Sprite,
                        module + "/" + node.LayerPath + "#" + node.LayerId)
                };
                // 文件名带上内容与九宫（Key），避免「图一样但九宫不同」的两张资源撞名。
                candidate.FileName = StableId.FileName(candidate.Name, candidate.Key, nine.Sprite.Width,
                    nine.Sprite.Height);

                SpriteExport existing;
                if (byContent.TryGetValue(candidate.Key, out existing))
                {
                    // 内容完全一样（含九宫）：共用同一张图，不再落盘
                    existing.NodeIds.Add(node.Id);
                    existing.NodeNames.Add(candidate.Name);
                    existing.Shared = true;
                    node.ResourceId = existing.ResourceId;
                    continue;
                }

                candidate.NodeIds.Add(node.Id);
                candidate.NodeNames.Add(candidate.Name);
                node.ResourceId = candidate.ResourceId;
                plan.Sprites.Add(candidate);
                byContent[candidate.Key] = candidate;

                List<SpriteExport> sameName;
                if (!byName.TryGetValue(candidate.Name, out sameName))
                {
                    sameName = new List<SpriteExport>();
                    byName[candidate.Name] = sameName;
                }

                sameName.Add(candidate);
            }

            ResolveReferences(document, options, plan, references, byName);
            BuildResources(document, options, plan);
            FillStats(document, plan);
            return plan;
        }

        /// <summary>
        /// 资源落在哪个模块目录。选项没指定时用文档自己的模块名，
        /// 这样「解析时定的模块」和「导出时落的目录」不会各说各话。
        /// </summary>
        public static string ModuleOf(UiDocument document, ExportOptions options)
        {
            string module = options == null ? null : options.Module;
            if (string.IsNullOrEmpty(module) && document != null)
            {
                module = document.Document.Module;
            }

            return string.IsNullOrEmpty(module) ? "common" : module;
        }

        /// <summary>哪些节点需要一张贴图：除了文本、纯色、空容器与忽略节点都要。</summary>
        public static bool NeedsSprite(UiNode node)
        {
            if (node == null)
            {
                return false;
            }

            switch (node.Type)
            {
                case UiElementType.Text:
                case UiElementType.TmpText:
                case UiElementType.FillColor:
                case UiElementType.Group:
                case UiElementType.Ignore:
                    return false;
                default:
                    return true;
            }
        }

        private static void ResolveReferences(UiDocument document, ExportOptions options, ExportPlan plan,
            List<UiNode> references, Dictionary<string, List<SpriteExport>> byName)
        {
            for (int i = 0; i < references.Count; i++)
            {
                UiNode node = references[i];
                string target = node.ReferenceTarget;
                List<SpriteExport> candidates;
                SpriteExport match = null;
                if (byName.TryGetValue(target, out candidates) && candidates.Count > 0)
                {
                    // 同名多张图时取第一张（按图层顺序），并提示存在歧义
                    match = candidates[0];
                    if (candidates.Count > 1)
                    {
                        document.Report(DiagnosticSeverity.Info, "resource.reference-ambiguous",
                            "引用的名字在本文件里有 " + candidates.Count + " 张图，取第一张：" + target, node);
                    }
                }

                if (match != null)
                {
                    match.Shared = true;
                    match.IsReferenced = true;
                    node.ResourceId = match.ResourceId;
                    plan.LocalReferences.Add(node);
                    continue;
                }

                // 本文件里没有：留给共享资源库按“模块/名字”去找（跨界面复用）
                node.ResourceId = StableId.ResourceId(UiResourceKind.Sprite, plan.Module + "/" + target);
                plan.ExternalReferences.Add(node);
                document.Report(DiagnosticSeverity.Warning, "resource.reference-external",
                    "引用目标不在本文件内，将按模块内的共享资源解析：" + target, node);
            }
        }

        private static void BuildResources(UiDocument document, ExportOptions options, ExportPlan plan)
        {
            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                SpriteExport sprite = plan.Sprites[i];
                plan.Resources.Add(new UiResource
                {
                    Id = sprite.ResourceId,
                    Kind = UiResourceKind.Sprite,
                    Name = sprite.Name,
                    Module = plan.Module,
                    FileName = sprite.FileName,
                    ContentHash = sprite.ContentHash,
                    Width = sprite.Bitmap.Width,
                    Height = sprite.Bitmap.Height,
                    Border = sprite.Border,
                    SourceLayerId = sprite.SourceLayerId,
                    SourceLayerPath = sprite.SourceLayerPath,
                    SourceRect = sprite.SourceRect,
                    Shared = sprite.Shared
                });
            }

            document.Resources.Clear();
            document.Resources.AddRange(plan.Resources);
        }

        private static void FillStats(UiDocument document, ExportPlan plan)
        {
            document.Stats["sprites"] = plan.Sprites.Count.ToString();
            document.Stats["spritesSliced"] = plan.SliceableCount.ToString();
            document.Stats["spriteShared"] = CountShared(plan).ToString();
            document.Stats["referencesLocal"] = plan.LocalReferences.Count.ToString();
            document.Stats["referencesExternal"] = plan.ExternalReferences.Count.ToString();
        }

        private static int CountShared(ExportPlan plan)
        {
            int count = 0;
            for (int i = 0; i < plan.Sprites.Count; i++)
            {
                if (plan.Sprites[i].Shared)
                {
                    count++;
                }
            }

            return count;
        }

        private static NineSliceResult Plain(Bitmap bitmap)
        {
            return new NineSliceResult
            {
                Sprite = bitmap,
                Border = new UiBorder(0, 0, 0, 0),
                SourceRect = new UiRect(0d, 0d, bitmap.Width, bitmap.Height),
                IsUniform = false,
                Reason = "未做九宫检测"
            };
        }
    }
}
