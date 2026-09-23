using System.Collections.Generic;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using UnityEditor;
using UnityEngine;

namespace Psd2Ugui.Editor.Build
{
    /// <summary>一次预制体产出（首次生成或增量更新）的统计。</summary>
    public sealed class PrefabProduct
    {
        public readonly PrefabBuildResult Build = new PrefabBuildResult();

        /// <summary>是不是走了增量更新（工程里本来就有这个预制体）。</summary>
        public bool Incremental;

        public int Created;
        public int Updated;
        public int Unchanged;
        public int Reparented;
        public int Removed;
        public int Kept;

        public string BuildSummary()
        {
            if (!Incremental)
            {
                return "新建预制体，" + Build.BuildSummary();
            }

            return "增量更新：新建 " + Created + "，刷新 " + Updated + "，未变 " + Unchanged +
                   "，换父级 " + Reparented + "，删除 " + Removed + "，保留人工内容 " + Kept +
                   " → " + Build.PrefabPath + "（共 " + Build.Nodes + " 个节点）";
        }
    }

    /// <summary>
    /// 把计划写进预制体：工程里没有就整棵新建，已经有了就按差异增量更新。
    ///
    /// 增量的意义在于「设计稿改一点点，人工调整全保住」：
    /// 只要节点还是同一个（稳定 ID），指纹没变就一根手指都不碰它，
    /// 人手动改过的名字、颜色、加过的脚本、挂过的子对象都原样留着。
    /// </summary>
    public static class IncrementalBuilder
    {
        public static PrefabProduct Build(UiDocument document, PlanNode plan, PrefabBuildContext context,
            string prefabPath)
        {
            var product = new PrefabProduct();
            if (document == null || plan == null || string.IsNullOrEmpty(prefabPath))
            {
                return product;
            }

            product.Build.PrefabPath = prefabPath;
            PrefabBuilder.EnsureFolder(prefabPath);

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing == null)
            {
                // 首次导出：整棵建起来
                product.Build.Root = null;
                PrefabBuildResult built = PrefabBuilder.BuildPrefab(document, plan, context, prefabPath);
                product.Build.Prefab = built.Prefab;
                product.Build.Nodes = built.Nodes;
                product.Created = built.Nodes;
                return product;
            }

            product.Incremental = true;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                context.Report(DiagnosticSeverity.Warning, "prefab.load-failed",
                    "预制体加载失败，已整棵重建：" + prefabPath);
                product.Incremental = false;
                PrefabBuildResult rebuilt = PrefabBuilder.BuildPrefab(document, plan, context, prefabPath);
                product.Build.Prefab = rebuilt.Prefab;
                product.Build.Nodes = rebuilt.Nodes;
                product.Created = rebuilt.Nodes;
                return product;
            }

            try
            {
                PrefabSnapshot.Result snapshot = PrefabSnapshot.Collect(root, plan, context);
                List<MergeOp> operations = PrefabMerge.Diff(snapshot.Nodes, plan);
                var index = new Dictionary<string, GameObject>();

                Restructure(operations, snapshot, root, context, index, product);
                Refresh(operations, context, index, product);
                Prune(operations, snapshot, root, context, product);

                product.Build.Nodes = index.Count;
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                product.Build.Prefab = saved;
                if (saved == null)
                {
                    context.Report(DiagnosticSeverity.Error, "prefab.save-failed",
                        "预制体保存失败：" + prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
                AssetDatabase.Refresh();
            }

            context.Report(DiagnosticSeverity.Info, "prefab.incremental", product.BuildSummary());
            return product;
        }

        /// <summary>
        /// 第一趟：只动结构。先让所有该存在的对象都就位（新建 / 换父级），
        /// 因为这一趟是按计划的先序遍历做的，轮到某个节点时它的父级一定已经就位。
        /// </summary>
        private static void Restructure(List<MergeOp> operations, PrefabSnapshot.Result snapshot,
            GameObject root, PrefabBuildContext context, Dictionary<string, GameObject> index,
            PrefabProduct product)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                MergeOp op = operations[i];
                if (op.Action == MergeAction.Remove || op.Action == MergeAction.Keep)
                {
                    continue;
                }

                if (op.ParentKey == null)
                {
                    // 根节点：无论计划怎么说，都写到现有根对象上
                    index[op.Key] = root;
                    continue;
                }

                GameObject parent = Parent(index, op, root, context);
                if (op.Action == MergeAction.Create)
                {
                    PrefabBuilder.CreateSubtree(op.Plan, parent.transform, context, index, op.ParentKey);
                    product.Created++;
                    continue;
                }

                GameObject go;
                if (!snapshot.Matched.TryGetValue(op.Key, out go))
                {
                    // 认领不到（计划里新加、又被判成更新）：当新增处理
                    PrefabBuilder.CreateSubtree(op.Plan, parent.transform, context, index, op.ParentKey);
                    product.Created++;
                    continue;
                }

                index[op.Key] = go;
                if (op.Action == MergeAction.Reparent)
                {
                    go.transform.SetParent(parent.transform, false);
                    product.Reparented++;
                }
            }
        }

        /// <summary>第二趟：刷属性。放到结构全部就位之后，连子件引用才能一次找齐。</summary>
        private static void Refresh(List<MergeOp> operations, PrefabBuildContext context,
            Dictionary<string, GameObject> index, PrefabProduct product)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                MergeOp op = operations[i];
                switch (op.Action)
                {
                    case MergeAction.Unchanged:
                        product.Unchanged++;
                        break;
                    case MergeAction.Create:
                        // 建的时候已经装配好了
                        break;
                    case MergeAction.Update:
                    case MergeAction.Reparent:
                        GameObject go;
                        if (index.TryGetValue(op.Key, out go))
                        {
                            PrefabBuilder.ApplyManaged(op.Plan, go, context, index, op.Key, op.ParentKey == null);
                            product.Updated++;
                        }

                        break;
                }
            }
        }

        /// <summary>第三趟：清理。放到最后，免得删掉的东西还被前面的连线引用。</summary>
        private static void Prune(List<MergeOp> operations, PrefabSnapshot.Result snapshot, GameObject root,
            PrefabBuildContext context, PrefabProduct product)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                MergeOp op = operations[i];
                if (op.Action == MergeAction.Keep)
                {
                    product.Kept++;
                    context.Report(DiagnosticSeverity.Info, "prefab.kept",
                        "设计稿里已经没有这个图层，但它带着人工内容，保留：" + op.Name);
                    continue;
                }

                if (op.Action != MergeAction.Remove)
                {
                    continue;
                }

                GameObject go;
                if (!snapshot.Stale.TryGetValue(op.Key, out go) || go == null)
                {
                    continue;
                }

                if (go == root)
                {
                    continue;
                }

                product.Removed++;
                Object.DestroyImmediate(go);
            }

            if (product.Removed > 0)
            {
                context.Report(DiagnosticSeverity.Info, "prefab.removed",
                    "设计稿里删掉的图层已从预制体里移除：" + product.Removed + " 个");
            }
        }

        private static GameObject Parent(Dictionary<string, GameObject> index, MergeOp op, GameObject root,
            PrefabBuildContext context)
        {
            GameObject parent;
            if (index.TryGetValue(op.ParentKey, out parent) && parent != null)
            {
                return parent;
            }

            context.Report(DiagnosticSeverity.Warning, "prefab.parent-missing",
                "找不到父节点，先挂到根下：" + op.Name + "（" + op.ParentKey + "）");
            return root;
        }
    }
}
