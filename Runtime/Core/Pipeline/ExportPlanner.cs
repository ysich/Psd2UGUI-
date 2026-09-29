using System;
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
            HashSet<UiNode> frameShared = FindSharedFrameNodes(document);

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

                if (frameShared.Contains(node))
                {
                    // 这个矩形不只是自己用（子节点定位、按钮状态图、ref 复用…）：
                    // 贴图得整张铺满它，只留内容框会被拉变形
                    nine = KeepLayerRect(nine, bitmap);
                }

                // 贴图比图层矩形小的时候记一笔：装配侧按它把节点矩形收进内容框。
                // 整张铺满的（本来就没裁、或者为别人补回了边）不记，契约里也不写。
                UiRect content = nine.SourceRect;
                node.ContentRect = CoversLayer(content, bitmap) ? (UiRect?)null : content;

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

                node.ResourceId = StableId.ResourceId(UiResourceKind.Sprite, plan.Module + "/" + target);

                SharedSprite shared = options.Shared == null ? null : options.Shared.Find(plan.Module, target);
                if (shared != null && ReuseShared(document, plan, node, shared))
                {
                    continue;
                }

                // 本文件里没有、共享资源库里也没有：只能报出来
                plan.ExternalReferences.Add(node);
                document.Report(DiagnosticSeverity.Warning, "resource.reference-external",
                    "引用目标不在本文件内，也没在共享资源库里找到，节点不会有贴图：" + target, node);
            }
        }

        /// <summary>
        /// 复用别的界面导出过的贴图：不落盘，只把身份信息记下来，
        /// 让契约里能找到它、让重新导出时的清理逻辑知道这张图还有人用。
        /// </summary>
        private static bool ReuseShared(UiDocument document, ExportPlan plan, UiNode node, SharedSprite shared)
        {
            for (int i = 0; i < plan.Reused.Count; i++)
            {
                if (plan.Reused[i].Id == node.ResourceId)
                {
                    plan.SharedReferences.Add(node);
                    return true;
                }
            }

            plan.Reused.Add(new UiResource
            {
                Id = node.ResourceId,
                Kind = UiResourceKind.Sprite,
                Name = shared.Name,
                Module = shared.Module,
                FileName = shared.FileName,
                ContentHash = shared.ContentHash,
                Width = shared.Width,
                Height = shared.Height,
                Border = shared.Border,
                Shared = true
            });
            plan.SharedReferences.Add(node);
            document.Report(DiagnosticSeverity.Info, "resource.reference-shared",
                "引用的是共享资源库里已有的图，直接复用：" + shared.Name + "（模块 " + shared.Module + "）", node);
            return true;
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

            // 复用来的共享贴图也要进资源表，否则装配时找不到它
            for (int i = 0; i < plan.Reused.Count; i++)
            {
                plan.Resources.Add(plan.Reused[i]);
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
            document.Stats["referencesShared"] = plan.SharedReferences.Count.ToString();
            document.Stats["spritesReused"] = plan.Reused.Count.ToString();
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

        /// <summary>
        /// 找出「矩形不只给自己用」的图层：这些贴图必须整张铺满图层矩形，不能只留内容框。
        ///
        /// 判据只看结构，不看装配计划：
        /// - 有子节点的容器：子节点按它的矩形定位；
        /// - 按钮的四个交互态，以及同一个框里的兄弟图层（含按钮自己的底图）：
        ///   uGUI 把它们都铺在同一张矩形里，谁被裁了都会跟别人错位；
        /// - Slider 的填充图：装配侧会把它改成 Filled 整张铺满，压过的图会铺歪；
        /// - Handle：uGUI 运行时会按滑块位置改 handleRect，贴图得跟着矩形走；
        /// - Viewport：滚动内容会被挂到它下面，矩形是内容的参照；
        /// - 被 `ref` 引用的图层：它的图是按别人的矩形铺的。
        /// </summary>
        private static HashSet<UiNode> FindSharedFrameNodes(UiDocument document)
        {
            var shared = new HashSet<UiNode>();
            var referenceTargets = new HashSet<string>();
            foreach (UiNode node in document.Nodes())
            {
                if (!string.IsNullOrEmpty(node.ReferenceTarget))
                {
                    referenceTargets.Add(node.ReferenceTarget);
                }
            }

            foreach (UiNode node in document.Nodes())
            {
                if (node.Children.Count > 0 || UiRoles.IsState(node.Role) || node.Role == UiRole.Fill ||
                    node.Role == UiRole.Handle || node.Role == UiRole.Viewport ||
                    (!string.IsNullOrEmpty(node.Name) && referenceTargets.Contains(node.Name)))
                {
                    shared.Add(node);
                }

                for (int i = 0; i < node.Children.Count; i++)
                {
                    if (!UiRoles.IsState(node.Children[i].Role))
                    {
                        continue;
                    }

                    // 按钮框：常态图与状态图铺在同一个矩形里，整框一起整张导
                    shared.Add(node);
                    for (int j = 0; j < node.Children.Count; j++)
                    {
                        shared.Add(node.Children[j]);
                    }

                    break;
                }
            }

            return shared;
        }

        /// <summary>
        /// 把去空边裁掉的那圈透明边补回来，九宫边框跟着往外挪，贴图重新铺满图层矩形。
        /// 这样按图层矩形铺回去的时候，画面与 PSD 仍然是 1:1。
        /// </summary>
        private static NineSliceResult KeepLayerRect(NineSliceResult nine, Bitmap layer)
        {
            UiRect box = nine.SourceRect;
            int left = (int)Math.Round(box.X);
            int top = (int)Math.Round(box.Y);
            int right = layer.Width - (int)Math.Round(box.Right);
            int bottom = layer.Height - (int)Math.Round(box.Bottom);
            if (left <= 0 && top <= 0 && right <= 0 && bottom <= 0)
            {
                // 本来就没裁掉东西，贴图已经是整张
                return nine;
            }

            nine.Sprite = Bitmap.Pad(nine.Sprite, left, top, right, bottom);
            nine.Border = new UiBorder(nine.Border.Left + left, nine.Border.Bottom + bottom,
                nine.Border.Right + right, nine.Border.Top + top);
            nine.SourceRect = new UiRect(0d, 0d, layer.Width, layer.Height);
            return nine;
        }

        /// <summary>贴图是不是整张盖住了图层矩形（一条边都没裁掉）。</summary>
        private static bool CoversLayer(UiRect box, Bitmap layer)
        {
            return box.X <= 0d && box.Y <= 0d && box.Right >= layer.Width && box.Bottom >= layer.Height;
        }
    }
}
