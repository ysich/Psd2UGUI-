using System.Collections.Generic;

namespace Psd2Ugui.Core.Build
{
    /// <summary>重新导出时对某个已有节点要做的操作。</summary>
    public enum MergeAction
    {
        /// <summary>新建（计划里有、工程里没有）。</summary>
        Create,

        /// <summary>合并到已有节点：结构对得上，但要按新值刷新受管属性。</summary>
        Update,

        /// <summary>完全不用动（计划与现状一致）。</summary>
        Unchanged,

        /// <summary>换个父节点（设计稿里调整了层级）。</summary>
        Reparent,

        /// <summary>从计划里消失了，删掉它。</summary>
        Remove,

        /// <summary>从计划里消失了，但它带着人手加的东西，保留下来。</summary>
        Keep
    }

    /// <summary>
    /// 计划节点的层级键：有稳定 ID 就用它，模板节点用 `#名字`。
    /// 父子关系也用键路径表示，这样重挂到模板节点下的子节点也能认出「爹没变」。
    /// </summary>
    public static class PlanKeys
    {
        public static string Key(PlanNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(node.SourceNodeId) ? "#" + node.Name : node.SourceNodeId;
        }

        public static string Join(string parentKey, string key)
        {
            return string.IsNullOrEmpty(parentKey) ? key : parentKey + "/" + key;
        }
    }

    /// <summary>工程里现存节点的一份快照（由 Editor 侧从标记组件读出）。</summary>
    public sealed class ExistingNode
    {
        public string NodeId;

        /// <summary>本节点在工程里的键路径（模板节点是 `#名字` 拼出来的路径）。</summary>
        public string Key;

        /// <summary>父节点在工程里的键路径，根为 null。</summary>
        public string ParentKey;

        public string Name;
        public string SpecHash = string.Empty;

        /// <summary>它或它的后代里有没有人手加的节点。</summary>
        public bool HasUserContent;

        /// <summary>它是不是工具生成的（带标记且带稳定 ID）。</summary>
        public bool Generated = true;
    }

    /// <summary>一次合并操作。</summary>
    public sealed class MergeOp
    {
        public MergeAction Action;
        public string NodeId = string.Empty;
        public string Name = string.Empty;

        /// <summary>父节点的键路径（用于找父对象）。</summary>
        public string ParentKey;

        /// <summary>本节点在计划里的键（含模板节点的 `#名字`）。</summary>
        public string Key = string.Empty;

        public PlanNode Plan;

        /// <summary>为什么这么处理，写进报告用。</summary>
        public string Reason = string.Empty;

        public override string ToString()
        {
            return Action + " " + (Plan == null ? Name : Plan.Name) + " (" + Reason + ")";
        }
    }

    /// <summary>
    /// 计划与现状的差异比对。
    ///
    /// 规则（都是为了「人工改动不被冲掉」）：
    /// - 计划里有的节点按稳定 ID 认领已有对象，指纹一样就完全不动，指纹变了才刷新属性；
    /// - 计划里没有的工具节点删掉，但它身上挂了人手加的子节点/组件时保留并提示；
    /// - 没有标记的节点一律当成人手加的，永远不碰。
    /// </summary>
    public static class PrefabMerge
    {
        public static List<MergeOp> Diff(IList<ExistingNode> existing, PlanNode planRoot)
        {
            var operations = new List<MergeOp>();
            var byId = new Dictionary<string, ExistingNode>();
            var byKey = new Dictionary<string, ExistingNode>();
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    ExistingNode node = existing[i];
                    if (node == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(node.NodeId) && !byId.ContainsKey(node.NodeId))
                    {
                        byId[node.NodeId] = node;
                    }

                    if (!string.IsNullOrEmpty(node.Key) && !byKey.ContainsKey(node.Key))
                    {
                        byKey[node.Key] = node;
                    }
                }
            }

            var claimed = new HashSet<ExistingNode>();
            DiffPlan(planRoot, null, byId, byKey, claimed, operations);

            if (existing == null)
            {
                return operations;
            }

            for (int i = 0; i < existing.Count; i++)
            {
                ExistingNode node = existing[i];
                if (node == null || !node.Generated || claimed.Contains(node))
                {
                    continue;
                }

                operations.Add(new MergeOp
                {
                    Action = node.HasUserContent ? MergeAction.Keep : MergeAction.Remove,
                    NodeId = node.NodeId ?? string.Empty,
                    Name = node.Name,
                    Key = string.IsNullOrEmpty(node.Key) ? node.NodeId : node.Key,
                    ParentKey = node.ParentKey,
                    Reason = node.HasUserContent
                        ? "设计稿里已经没有它，但挂着人手加的子节点，保留"
                        : "设计稿里已经没有它"
                });
            }

            return operations;
        }

        private static void DiffPlan(PlanNode plan, string parentKey, Dictionary<string, ExistingNode> byId,
            Dictionary<string, ExistingNode> byKey, HashSet<ExistingNode> claimed, List<MergeOp> operations)
        {
            if (plan == null)
            {
                return;
            }

            string nodeId = plan.SourceNodeId;
            string key = PlanKeys.Join(parentKey, PlanKeys.Key(plan));

            // 先按稳定 ID 认领（这样「被人挪过位置」也能认出来）；
            // 模板节点没有 ID，退回按键路径认领。
            ExistingNode current = null;
            if (!string.IsNullOrEmpty(nodeId) && byId.TryGetValue(nodeId, out current) && claimed.Contains(current))
            {
                current = null;
            }

            if (current == null && byKey.TryGetValue(key, out current) && claimed.Contains(current))
            {
                current = null;
            }

            if (current != null)
            {
                claimed.Add(current);
                bool moved = current.ParentKey != parentKey;
                bool changed = current.SpecHash != PlanSpec.Hash(plan);
                operations.Add(new MergeOp
                {
                    Action = moved ? MergeAction.Reparent : (changed ? MergeAction.Update : MergeAction.Unchanged),
                    NodeId = nodeId,
                    Name = plan.Name,
                    Key = key,
                    ParentKey = parentKey,
                    Plan = plan,
                    Reason = moved ? "层级变了" : (changed ? "设计稿里的属性变了" : "没有变化")
                });
            }
            else
            {
                operations.Add(new MergeOp
                {
                    Action = MergeAction.Create,
                    NodeId = nodeId ?? string.Empty,
                    Name = plan.Name,
                    Key = key,
                    ParentKey = parentKey,
                    Plan = plan,
                    Reason = string.IsNullOrEmpty(nodeId) ? "模板节点" : "新增的图层"
                });
            }

            for (int i = 0; i < plan.Children.Count; i++)
            {
                DiffPlan(plan.Children[i], key, byId, byKey, claimed, operations);
            }
        }
    }
}
