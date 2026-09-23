using System.Collections.Generic;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Psd2Ugui.Editor.Build
{
    /// <summary>
    /// 把现有预制体读成一份「现状快照」，交给 <see cref="PrefabMerge"/> 比对。
    ///
    /// 认领规则：
    /// - 带标记的节点按稳定 ID 认领，同时记下它「现在」挂在哪个父节点下，用来发现被人挪过位置；
    /// - 模板节点（工具补出来的 Viewport / Content 之类，没有稳定 ID）按同父下的同名子对象认领；
    /// - 认领不到的带标记节点算「设计稿里已经删了」；没标记的对象一律当人手加的，绝不碰。
    /// </summary>
    public static class PrefabSnapshot
    {
        public sealed class Result
        {
            /// <summary>现状快照，喂给 <see cref="PrefabMerge.Diff"/>。</summary>
            public readonly List<ExistingNode> Nodes = new List<ExistingNode>();

            /// <summary>计划键路径 → 认领到的对象，合并时按这个找对象。</summary>
            public readonly Dictionary<string, GameObject> Matched = new Dictionary<string, GameObject>();

            /// <summary>计划里已经没有的旧节点：键路径 → 对象。</summary>
            public readonly Dictionary<string, GameObject> Stale = new Dictionary<string, GameObject>();
        }

        public static Result Collect(GameObject root, PlanNode plan, PrefabBuildContext context)
        {
            var result = new Result();
            if (root == null || plan == null)
            {
                return result;
            }

            Psd2UguiNode[] markers = root.GetComponentsInChildren<Psd2UguiNode>(true);
            var byId = new Dictionary<string, GameObject>();
            var duplicates = new HashSet<string>();
            for (int i = 0; i < markers.Length; i++)
            {
                Psd2UguiNode marker = markers[i];
                if (marker == null || string.IsNullOrEmpty(marker.NodeId))
                {
                    continue;
                }

                if (byId.ContainsKey(marker.NodeId))
                {
                    duplicates.Add(marker.NodeId);
                }
                else
                {
                    byId[marker.NodeId] = marker.gameObject;
                }
            }

            var keyOfTransform = new Dictionary<Transform, string>();
            string rootKey = PlanKeys.Key(plan);
            keyOfTransform[root.transform] = rootKey;

            // 根节点永远认领现有根对象：换根等于把整个预制体重建一遍，还要用户重连引用，代价太大
            var rootMarker = root.GetComponent<Psd2UguiNode>();
            result.Matched[rootKey] = root;
            result.Nodes.Add(Entry(root, plan.SourceNodeId, rootKey, null, rootMarker));

            var claimed = new HashSet<GameObject>();
            claimed.Add(root);
            for (int i = 0; i < plan.Children.Count; i++)
            {
                Walk(plan.Children[i], root.transform, rootKey, byId, keyOfTransform, claimed, result);
            }

            CollectStale(markers, byId, duplicates, claimed, keyOfTransform, result, context);
            return result;
        }

        private static void Walk(PlanNode plan, Transform parent, string parentKey,
            Dictionary<string, GameObject> byId, Dictionary<Transform, string> keyOfTransform,
            HashSet<GameObject> claimed, Result result)
        {
            string key = PlanKeys.Join(parentKey, PlanKeys.Key(plan));
            GameObject go = null;
            if (!string.IsNullOrEmpty(plan.SourceNodeId))
            {
                byId.TryGetValue(plan.SourceNodeId, out go);
            }
            else
            {
                // 模板节点：按同父下的同名子对象认领
                go = FindChildByName(parent, plan.Name);
            }

            if (go == null || claimed.Contains(go))
            {
                // 计划里有、工程里没有（或这个对象已经被别的计划节点认领了）：交给 Diff 当新增
                return;
            }

            claimed.Add(go);
            result.Matched[key] = go;
            keyOfTransform[go.transform] = key;
            result.Nodes.Add(Entry(go, plan.SourceNodeId, key, ActualParentKey(go, keyOfTransform),
                go.GetComponent<Psd2UguiNode>()));

            for (int i = 0; i < plan.Children.Count; i++)
            {
                Walk(plan.Children[i], go.transform, key, byId, keyOfTransform, claimed, result);
            }
        }

        /// <summary>认领不到的带标记节点：计划里已经没有它们了。</summary>
        private static void CollectStale(Psd2UguiNode[] markers, Dictionary<string, GameObject> byId,
            HashSet<string> duplicates, HashSet<GameObject> claimed, Dictionary<Transform, string> keyOfTransform,
            Result result, PrefabBuildContext context)
        {
            for (int i = 0; i < markers.Length; i++)
            {
                Psd2UguiNode marker = markers[i];
                GameObject go = marker == null ? null : marker.gameObject;
                if (go == null || claimed.Contains(go))
                {
                    continue;
                }

                bool userOwned = duplicates.Contains(marker.NodeId) ||
                                 (byId.ContainsKey(marker.NodeId) && claimed.Contains(byId[marker.NodeId]));
                if (userOwned)
                {
                    // 同一个节点被复制成了好几份：这多半是人手动复制出来当模板用的，别删
                    context.Report(DiagnosticSeverity.Info, "prefab.duplicate-node",
                        "发现同一个生成节点的副本，按人工内容保留：" + go.name + " (" + marker.NodeId + ")");
                }

                string key = IdentityPath(go.transform, keyOfTransform);
                while (result.Stale.ContainsKey(key))
                {
                    key += "*";
                }

                result.Stale[key] = go;
                result.Nodes.Add(new ExistingNode
                {
                    NodeId = marker.NodeId,
                    Key = key,
                    ParentKey = ActualParentKey(go, keyOfTransform),
                    Name = go.name,
                    SpecHash = marker.SpecHash,
                    Generated = !userOwned,
                    HasUserContent = HasUserContent(go)
                });
            }
        }

        private static ExistingNode Entry(GameObject go, string nodeId, string key, string parentKey,
            Psd2UguiNode marker)
        {
            return new ExistingNode
            {
                NodeId = nodeId ?? string.Empty,
                Key = key,
                ParentKey = parentKey,
                Name = go.name,
                SpecHash = marker == null || marker.SpecHash == null ? string.Empty : marker.SpecHash,
                Generated = true,
                HasUserContent = HasUserContent(go)
            };
        }

        private static GameObject FindChildByName(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>节点「现在」挂在谁下面（不是计划里应该挂在谁下面）。</summary>
        private static string ActualParentKey(GameObject go, Dictionary<Transform, string> keyOfTransform)
        {
            Transform parent = go.transform.parent;
            if (parent == null)
            {
                return null;
            }

            string key;
            return keyOfTransform.TryGetValue(parent, out key) ? key : IdentityPath(parent, keyOfTransform);
        }

        /// <summary>
        /// 按对象自己的名字/标记拼一条键路径出来，用在「没被计划认领的父节点」上，
        /// 这样人手加的中间层也能表达。
        /// </summary>
        private static string IdentityPath(Transform transform, Dictionary<Transform, string> keyOfTransform)
        {
            var parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                string known;
                if (keyOfTransform.TryGetValue(current, out known))
                {
                    parts.Reverse();
                    return PlanKeys.Join(known, string.Join("/", parts.ToArray()));
                }

                var marker = current.GetComponent<Psd2UguiNode>();
                parts.Add(marker != null && !string.IsNullOrEmpty(marker.NodeId)
                    ? marker.NodeId
                    : "#" + current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        /// <summary>
        /// 这个节点上有没有人手加的东西。
        /// 判据：挂着工具不会生成的组件，或者带着没标记的子对象（子预制体实例除外）。
        /// 宁可多留不可误删，所以判据取宽。
        /// </summary>
        public static bool HasUserContent(GameObject go)
        {
            if (HasForeignComponent(go))
            {
                return true;
            }

            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform child = all[i];
                if (child == go.transform || PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                {
                    continue;
                }

                if (child.GetComponent<Psd2UguiNode>() == null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>工具会生成的组件白名单；白名单之外的组件一律当成人工内容。</summary>
        private static readonly System.Type[] GeneratedComponents =
        {
            typeof(Transform), typeof(RectTransform), typeof(CanvasRenderer), typeof(Psd2UguiNode),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup),
            typeof(RectMask2D), typeof(Mask), typeof(Shadow), typeof(Outline), typeof(LayoutElement),
            typeof(Image), typeof(RawImage), typeof(Text), typeof(Button), typeof(Toggle), typeof(Slider),
            typeof(Scrollbar), typeof(ScrollRect), typeof(Dropdown), typeof(InputField)
        };

        private static bool HasForeignComponent(GameObject go)
        {
            Component[] components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                // TMP 在可选程序集里，这里只能按名字认
                if (component.GetType().Name == "TextMeshProUGUI")
                {
                    continue;
                }

                bool known = false;
                for (int j = 0; j < GeneratedComponents.Length; j++)
                {
                    if (GeneratedComponents[j].IsInstanceOfType(component))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
