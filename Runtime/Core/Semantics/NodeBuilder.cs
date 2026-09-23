using System;
using System.Collections.Generic;
using System.Text;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Psd;

namespace Psd2Ugui.Core.Semantics
{
    /// <summary>构建契约节点树的可调项。</summary>
    public sealed class NodeBuildOptions
    {
        public string Module = "common";

        /// <summary>没写标签的文本层按哪种文本控件导出。</summary>
        public UiElementType TextType = UiElementType.TmpText;

        /// <summary>分组默认导出成 Panel 还是空 Group。</summary>
        public bool GroupsAsPanel;

        /// <summary>隐藏图层是否也进入节点树（导出时可以再按 Visible 决定是否激活）。</summary>
        public bool KeepHiddenLayers = true;

        public bool InferTypes = true;

        public bool InferRoles = true;

        /// <summary>人工修正表，优先级高于标签与推断。</summary>
        public NodeOverrides Overrides;

        /// <summary>额外的推断器，按顺序在启发式规则之前询问。</summary>
        public readonly List<ITypeInferrer> Inferrers = new List<ITypeInferrer>();
    }

    /// <summary>
    /// PSD 图层树 → 契约节点树。
    /// 这里只做“结构 + 语义”，不碰像素：位图导出、九宫、Prefab 装配都在后面的步骤里。
    /// </summary>
    public static class NodeBuilder
    {
        public static UiDocument Build(PsdFile file, NodeBuildOptions options = null)
        {
            if (file == null)
            {
                return null;
            }

            options = options ?? new NodeBuildOptions();
            var document = new UiDocument();
            document.Document = new PsdMeta
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(file.FileName),
                FileName = file.FileName,
                Module = options.Module,
                Width = file.Width,
                Height = file.Height,
                ChannelCount = file.ChannelCount,
                BitDepth = file.BitDepth,
                ColorMode = file.ColorMode,
                LayerCount = file.Layers.Count,
                ResolutionPpi = file.ResolutionPpi
            };
            for (int i = 0; i < file.Warnings.Count; i++)
            {
                document.Report(DiagnosticSeverity.Warning, "psd.warning", file.Warnings[i]);
            }

            var state = new BuildState();
            var root = new UiNode
            {
                Id = StableId.NodeId("__root__", -1),
                Name = document.Document.Name,
                LayerPath = string.Empty,
                LayerId = -1,
                Type = UiElementType.Group,
                Rect = new UiRect(0d, 0d, file.Width, file.Height),
                Visible = true,
                Opacity = 1d
            };
            document.Root = root;

            var heuristic = new HeuristicTypeInferrer(options.TextType,
                options.GroupsAsPanel ? UiElementType.Panel : UiElementType.Group);
            int order = 0;
            for (int i = 0; i < file.RootLayers.Count; i++)
            {
                UiNode node = BuildNode(file, file.RootLayers[i], root, string.Empty, 1, options, heuristic, state,
                    document);
                if (node != null)
                {
                    root.Children.Add(node);
                    order++;
                }

            }

            if (options.Overrides != null)
            {
                Reparent(document, options.Overrides, state);
                ReportUnmatchedOverrides(document, options.Overrides, state);
            }

            FillStats(document, state);
            return document;
        }

        /// <summary>覆盖表里可以分开写的字段，取“最后一条命中”的值。</summary>
        private enum OverrideField
        {
            Type,
            Role,
            Name,
            Resource,
            NineSlice
        }

        private static NodeOverride Last(List<NodeOverride> matches, OverrideField field)
        {
            if (matches == null)
            {
                return null;
            }

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                NodeOverride item = matches[i];
                switch (field)
                {
                    case OverrideField.Type:
                        if (item.HasType)
                        {
                            return item;
                        }

                        break;
                    case OverrideField.Role:
                        if (item.HasRole)
                        {
                            return item;
                        }

                        break;
                    case OverrideField.Name:
                        if (item.Name != null)
                        {
                            return item;
                        }

                        break;
                    case OverrideField.Resource:
                        if (item.Resource != null)
                        {
                            return item;
                        }

                        break;
                    case OverrideField.NineSlice:
                        if (item.HasNineSlice)
                        {
                            return item;
                        }

                        break;
                }
            }

            return null;
        }

        private static bool HasIgnore(List<NodeOverride> matches)
        {
            if (matches == null)
            {
                return false;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Ignore)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class BuildState
        {
            public int Nodes;
            public int Groups;
            public int Tagged;
            public int Inferred;
            public int Overridden;
            public int Ignored;
            public int Hidden;
            public int References;
            public readonly Dictionary<string, int> Types = new Dictionary<string, int>();
            public readonly List<int> MatchedOverrideIndices = new List<int>();


            public void CountType(UiElementType type)
            {
                string key = type.ToContract();
                int count;
                Types.TryGetValue(key, out count);
                Types[key] = count + 1;
                if (type == UiElementType.Group || type == UiElementType.Panel)
                {
                    Groups++;
                }
            }
        }

        private static UiNode BuildNode(PsdFile file, PsdLayer layer, UiNode parent, string parentPath, int depth,
            NodeBuildOptions options, HeuristicTypeInferrer heuristic, BuildState state, UiDocument document)
        {
            if (layer.IsBoundingDivider)
            {
                return null;
            }

            LayerTag tag = LayerTagParser.Parse(layer.DisplayName);
            string path = parentPath.Length == 0 ? tag.Name : parentPath + "/" + tag.Name;

            List<NodeOverride> matches = options.Overrides == null
                ? null
                : options.Overrides.MatchAll(path, layer.LayerId, tag.Name, state.MatchedOverrideIndices);
            NodeOverride entry = Last(matches, OverrideField.Type);

            if (tag.Ignored || HasIgnore(matches))
            {
                state.Ignored++;
                document.Report(DiagnosticSeverity.Info, "node.ignored",
                    "图层被忽略，不进入节点树：" + layer.DisplayName, null);
                return null;
            }

            if (!layer.Visible)
            {
                state.Hidden++;
                if (!options.KeepHiddenLayers)
                {
                    return null;
                }
            }

            var node = new UiNode
            {
                LayerId = layer.LayerId,
                Name = tag.Name,
                LayerPath = path,
                Rect = layer.Rect,
                Visible = layer.Visible,
                Opacity = layer.Opacity / 255d,
                Clipping = layer.Clipping,
                SectionKind = layer.SectionKind,
                IsPrefabReference = tag.IsPrefabReference
            };
            node.Id = StableId.NodeId(path, layer.LayerId);

            if (tag.NineSlice)
            {
                node.Tags["nine-slice"] = "true";
            }

            if (tag.UnknownTokens.Count > 0)
            {
                node.Tags["unknown-tags"] = string.Join(",", tag.UnknownTokens.ToArray());
                document.Report(DiagnosticSeverity.Info, "tag.unknown",
                    "图层名里有不认识的标签：" + layer.DisplayName + " -> " +
                    string.Join(",", tag.UnknownTokens.ToArray()), node);
            }

            if (tag.IsReference)
            {
                node.ReferenceTarget = tag.ReferenceTarget;
                state.References++;
                node.Tags[tag.IsPrefabReference ? "refp" : "ref"] = tag.ReferenceTarget;
            }

            if (layer.Text != null && layer.Text.HasValue)
            {
                node.Text = BuildText(layer.Text);
            }

            if (layer.HasSolidFill)
            {
                node.HasFill = true;
                node.Fill = layer.SolidFill;
            }

            for (int i = 0; i < layer.Effects.Count; i++)
            {
                node.Effects.Add(layer.Effects[i]);
            }

            // 类型：覆盖表 > 标签 > 自定义推断器 > 启发式
            UiElementType type = UiElementType.None;
            if (entry != null && entry.HasType)
            {
                type = entry.Type;
                state.Overridden++;
            }

            else if (tag.Type != UiElementType.None)
            {
                type = tag.Type;
                state.Tagged++;
            }
            else if (options.InferTypes)
            {
                var context = new InferenceContext
                {
                    Layer = layer,
                    Tag = tag,
                    Depth = depth,
                    TaggedType = tag.Type,
                    TaggedRole = tag.Role,
                    IsGroup = layer.IsGroup,
                    HasText = layer.Text != null && layer.Text.HasValue,
                    HasPixelData = layer.HasPixelData,
                    HasSolidFill = layer.HasSolidFill,
                    IsReference = tag.IsReference
                };
                for (int i = 0; i < options.Inferrers.Count && type == UiElementType.None; i++)
                {
                    type = options.Inferrers[i].Infer(context);
                }

                if (type == UiElementType.None)
                {
                    type = heuristic.Infer(context);
                }

                if (type != UiElementType.None)
                {
                    state.Inferred++;
                }
            }

            node.Type = type;
            if (node.Type == UiElementType.Ignore || tag.Role == UiRole.Ignore)
            {
                state.Ignored++;
                document.Report(DiagnosticSeverity.Info, "node.ignored",
                    "图层被 ignore 标签跳过：" + layer.DisplayName, null);
                return null;
            }

            NodeOverride rename = Last(matches, OverrideField.Name);
            if (rename != null)
            {
                node.Name = rename.Name;
            }

            NodeOverride resource = Last(matches, OverrideField.Resource);
            if (resource != null)
            {
                node.ReferenceTarget = resource.Resource;
            }

            NodeOverride nineSlice = Last(matches, OverrideField.NineSlice);
            if (nineSlice != null)
            {
                if (nineSlice.NineSlice)
                {
                    node.Tags["nine-slice"] = "true";
                }
                else
                {
                    node.Tags.Remove("nine-slice");
                }
            }

            // 角色：覆盖表 > 标签 > 父级结构
            UiRole role = UiRole.None;
            NodeOverride roleEntry = Last(matches, OverrideField.Role);
            if (roleEntry != null)
            {
                role = roleEntry.Role;
                state.Overridden++;
            }

            else if (tag.Role != UiRole.None)
            {
                role = tag.Role;
                state.Tagged++;
            }
            else if (options.InferRoles)
            {
                role = InferRole(node, parent);
            }

            node.Role = role;
            state.Nodes++;
            state.CountType(node.Type);

            string childPath = parentPath.Length == 0 ? node.Name : parentPath + "/" + node.Name;
            for (int i = 0; i < layer.Children.Count; i++)
            {
                UiNode child = BuildNode(file, layer.Children[i], node, childPath, depth + 1, options, heuristic, state,
                    document);
                if (child != null)
                {
                    node.Children.Add(child);
                }

            }

            return node;
        }

        /// <summary>没写标签时按父级结构补角色：按钮里的文字就是按钮文字，输入框里的文字就是输入文字。</summary>
        private static UiRole InferRole(UiNode node, UiNode parent)
        {
            if (parent == null)
            {
                return UiRole.None;
            }

            bool isText = node.Type == UiElementType.Text || node.Type == UiElementType.TmpText;
            switch (parent.Type)
            {
                case UiElementType.Button:
                case UiElementType.TmpButton:
                case UiElementType.Toggle:
                case UiElementType.TmpToggle:
                case UiElementType.Dropdown:
                case UiElementType.TmpDropdown:
                    if (isText)
                    {
                        return UiRole.ButtonText;
                    }

                    break;
                case UiElementType.InputField:
                case UiElementType.TmpInputField:
                    if (isText)
                    {
                        string name = (node.Name ?? string.Empty).ToLowerInvariant();
                        return name.IndexOf("placeholder", StringComparison.Ordinal) >= 0 ||
                               name.IndexOf("tips", StringComparison.Ordinal) >= 0 ||
                               name.IndexOf("hint", StringComparison.Ordinal) >= 0
                            ? UiRole.Placeholder
                            : UiRole.InputText;
                    }

                    break;
            }

            return UiRole.None;
        }

        private static UiTextInfo BuildText(PsdTextEngineInfo info)
        {
            var text = new UiTextInfo
            {
                HasValue = true,
                Content = info.Text ?? string.Empty,
                FontSize = info.FontSize,
                Color = info.HasColor ? info.Color : UiColor.White,
                FontName = info.FontName ?? string.Empty,
                FontKey = StableId.Sanitize(info.FontName ?? string.Empty).ToLowerInvariant(),
                Align = string.IsNullOrEmpty(info.Justification) ? "left" : info.Justification,
                WordWrap = info.AutoLeading > 0d || info.Leading > 0d,
                LineSpacing = info.Leading,
                Tracking = info.Tracking,
                PointText = false
            };
            return text;
        }

        /// <summary>按覆盖表把节点挪到新的父节点下。</summary>
        private static void Reparent(UiDocument document, NodeOverrides overrides, BuildState state)
        {
            for (int i = 0; i < overrides.Entries.Count; i++)
            {
                NodeOverride entry = overrides.Entries[i];
                if (string.IsNullOrEmpty(entry.Parent) || entry.Ignore)
                {
                    continue;
                }

                UiNode moving = FindByOverride(document.Root, entry);
                if (moving == null)
                {
                    continue;
                }

                UiNode target = FindByKey(document.Root, entry.Parent);
                if (target == null)
                {
                    document.Report(DiagnosticSeverity.Warning, "override.parent-missing",
                        "覆盖表指定的父节点不存在：" + entry.Key + " -> " + entry.Parent);
                    continue;
                }

                if (moving == target || IsAncestor(moving, target))
                {
                    document.Report(DiagnosticSeverity.Warning, "override.parent-cycle",
                        "覆盖表造成了循环层级，已忽略：" + entry.Key + " -> " + entry.Parent);
                    continue;
                }

                Remove(document.Root, moving);
                target.Children.Add(moving);
                state.Overridden++;
            }
        }

        private static void ReportUnmatchedOverrides(UiDocument document, NodeOverrides overrides, BuildState state)
        {
            for (int i = 0; i < overrides.Entries.Count; i++)
            {
                if (!state.MatchedOverrideIndices.Contains(i))

                {
                    document.Report(DiagnosticSeverity.Warning, "override.unmatched",
                        "覆盖表条目没有匹配到任何图层：" + overrides.Entries[i].Key);
                }
            }
        }

        private static UiNode FindByOverride(UiNode root, NodeOverride entry)
        {
            if (root == null)
            {
                return null;
            }

            foreach (UiNode node in root.SelfAndDescendants())
            {
                if (entry.Matches(node.LayerPath, node.LayerId, node.Name))
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>按 `#图层ID` / 路径 / 名字找节点。</summary>
        public static UiNode FindByKey(UiNode root, string key)
        {
            if (root == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (key[0] == '#')
            {
                if (int.TryParse(key.Substring(1), out int layerId))
                {
                    foreach (UiNode node in root.SelfAndDescendants())
                    {
                        if (node.LayerId == layerId)
                        {
                            return node;
                        }
                    }
                }

                return null;
            }

            if (key.IndexOf('/') >= 0)
            {
                foreach (UiNode node in root.SelfAndDescendants())
                {
                    if (string.Equals(node.LayerPath, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return node;
                    }
                }

                return null;
            }

            foreach (UiNode node in root.SelfAndDescendants())
            {
                if (string.Equals(node.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }
            }

            return null;
        }

        private static bool Remove(UiNode parent, UiNode target)
        {
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (parent.Children[i] == target)
                {
                    parent.Children.RemoveAt(i);
                    return true;
                }

                if (Remove(parent.Children[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAncestor(UiNode possibleAncestor, UiNode node)
        {
            foreach (UiNode item in possibleAncestor.SelfAndDescendants())
            {
                if (item == node)
                {
                    return true;
                }
            }

            return false;
        }

        private static void FillStats(UiDocument document, BuildState state)
        {
            var types = new List<string>(state.Types.Keys);
            types.Sort(StringComparer.Ordinal);
            var summary = new StringBuilder();
            for (int i = 0; i < types.Count; i++)
            {
                if (summary.Length > 0)
                {
                    summary.Append(", ");
                }

                summary.Append(types[i]).Append('=').Append(state.Types[types[i]]);
            }

            document.Stats["nodes"] = state.Nodes.ToString();
            document.Stats["groups"] = state.Groups.ToString();
            document.Stats["types"] = summary.ToString();
            document.Stats["tagged"] = state.Tagged.ToString();
            document.Stats["inferred"] = state.Inferred.ToString();
            document.Stats["overridden"] = state.Overridden.ToString();
            document.Stats["ignored"] = state.Ignored.ToString();
            document.Stats["hidden"] = state.Hidden.ToString();
            document.Stats["references"] = state.References.ToString();
        }
    }
}
