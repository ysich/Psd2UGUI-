using UnityEngine;

namespace Psd2Ugui
{
    /// <summary>
    /// 生成出来的节点上都挂这个标记，记录它来自哪个 PSD 图层。
    ///
    /// 它有两层用处：
    /// 1. 重新导出时按 <see cref="NodeId"/> 认领旧对象，把「工具生成的」与「人手加的」分开，
    ///    只覆盖自己管的属性，人工改动不会被冲掉；
    /// 2. 排查问题时能一眼看出这个节点对应 PSD 里的哪一层。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class Psd2UguiNode : MonoBehaviour
    {
        /// <summary>契约里的稳定节点 ID（同一个 PSD 反复导出不变）。</summary>
        public string NodeId;

        /// <summary>图层路径，例如 `Panel/Button/Icon`。</summary>
        public string LayerPath;

        /// <summary>PSD 图层 ID。</summary>
        public int LayerId = -1;

        /// <summary>来源 PSD 文件名，跨文件排查用。</summary>
        public string SourcePsd;

        /// <summary>角色标签（bg / fill / handle / …）。</summary>
        public string Role;

        /// <summary>生成器标识，方便将来识别旧版本产物。</summary>
        public string Generator = "psd2ugui";

        /// <summary>按节点 ID 找子节点（含自己）。</summary>
        public static Psd2UguiNode Find(Transform root, string nodeId)
        {
            if (root == null || string.IsNullOrEmpty(nodeId))
            {
                return null;
            }

            Psd2UguiNode[] all = root.GetComponentsInChildren<Psd2UguiNode>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].NodeId == nodeId)
                {
                    return all[i];
                }
            }

            return null;
        }
    }
}
