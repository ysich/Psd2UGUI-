using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Psd;

namespace Psd2Ugui.Core.Semantics
{
    /// <summary>
    /// 推断一个图层要变成什么控件。这里刻意只暴露“人看得懂”的输入，
    /// 方便将来接入别的推断实现（本地词表、外部服务、模型）而不改流水线。
    /// </summary>
    public sealed class InferenceContext
    {
        public PsdLayer Layer;
        public LayerTag Tag;
        public int Depth;

        /// <summary>标签显式指定的类型（None 表示没写标签）。</summary>
        public UiElementType TaggedType = UiElementType.None;

        /// <summary>标签显式指定的角色（None 表示没写标签）。</summary>
        public UiRole TaggedRole = UiRole.None;

        public bool IsGroup;
        public bool HasText;
        public bool HasPixelData;
        public bool HasSolidFill;
        public bool IsReference;

        /// <summary>便于诊断：描述当前图层。</summary>
        public string Describe()
        {
            return (Layer == null ? "(未知图层)" : Layer.DisplayName) + " 深度=" + Depth +
                   (HasText ? " 文本层" : string.Empty);
        }
    }

    /// <summary>控件类型推断扩展点。返回 None 表示“没有意见”，交给下一个推断器。</summary>
    public interface ITypeInferrer
    {
        string Name { get; }

        UiElementType Infer(InferenceContext context);
    }
}
