using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Build
{
    /// <summary>一个 GameObject 上要挂什么控件。</summary>
    public enum ControlKind
    {
        /// <summary>只有 RectTransform（分组、容器）。</summary>
        Rect,

        /// <summary>Image + 贴图。</summary>
        Image,

        /// <summary>RawImage + 贴图。</summary>
        RawImage,

        /// <summary>uGUI Text。</summary>
        Text,

        /// <summary>TextMeshPro（没装 TMP 时回退 Text）。</summary>
        TmpText,

        /// <summary>纯色块：Image 只给颜色不给贴图。</summary>
        FillColor,

        Button,
        Toggle,
        /// <summary>ToggleGroup 组件。</summary>
        ToggleGroup,
        /// <summary>GridLayoutGroup 组件。</summary>
        Grid,
        Slider,

        /// <summary>滚动条（ScrollRect 的 verticalScrollbar / horizontalScrollbar）。</summary>
        Scrollbar,

        ScrollView,
        Dropdown,
        InputField,

        /// <summary>Panel：带底图的容器，可选矩形遮罩。</summary>
        Panel,

        /// <summary>Mask：矩形/贴图遮罩容器。</summary>
        Mask
    }

    /// <summary>需要确定方向的控件（Slider / Scrollbar）用来选朝向。</summary>
    public enum PlanAxis
    {
        Horizontal,
        Vertical
    }

    /// <summary>
    /// 复合控件的状态图（按钮的常态/悬停/按下/选中/禁用）。
    /// 这些贴图在 PSD 里是独立图层，但在 uGUI 里只是 ``interactable`` 的颜色/贴图开关，
    /// 所以它们会被“吸收”进来，不再单独生成 GameObject。
    /// </summary>
    public sealed class PlanSpriteState
    {
        public string NormalId;
        public string NormalFile;
        public string HighlightedId;
        public string HighlightedFile;
        public string PressedId;
        public string PressedFile;
        public string SelectedId;
        public string SelectedFile;
        public string DisabledId;
        public string DisabledFile;

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrEmpty(HighlightedId) && string.IsNullOrEmpty(PressedId) &&
                       string.IsNullOrEmpty(SelectedId) && string.IsNullOrEmpty(DisabledId);
            }
        }
    }

    /// <summary>
    /// 模板节点的锚点写法。为 null 时按「左上角锚点 + 绝对尺寸」放置，
    /// 否则用 anchorMin/anchorMax + offsetMin/offsetMax（负的 Right/Top 表示向内收）。
    /// </summary>
    public sealed class PlanAnchor
    {
        public double MinX;
        public double MinY;
        public double MaxX = 1d;
        public double MaxY = 1d;
        public double Left;
        public double Bottom;
        public double Right;
        public double Top;

        public static PlanAnchor Stretch(double minX, double minY, double maxX, double maxY,
            double left = 0d, double bottom = 0d, double right = 0d, double top = 0d)
        {
            return new PlanAnchor
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Left = left,
                Bottom = bottom,
                Right = right,
                Top = top
            };
        }

        /// <summary>水平拉伸、垂直贴顶：滚动内容的标准写法。</summary>
        public static PlanAnchor TopStretch(double height)
        {
            return new PlanAnchor
            {
                MinX = 0d,
                MinY = 1d,
                MaxX = 1d,
                MaxY = 1d,
                Bottom = -height
            };
        }
    }

    /// <summary>
    /// 预制体装配计划里的一个节点。它是契约节点树到 Unity 对象树的中间层：
    /// 纯数据、能脱离 Unity 断言，Editor 侧只负责照着它创建对象与连线。
    /// </summary>
    public sealed class PlanNode
    {
        public string Name = "Node";

        /// <summary>对应契约里的节点 ID（稳定身份）；模板生成的辅助节点为 null。</summary>
        public string SourceNodeId;

        public string SourceLayerPath = string.Empty;
        public int SourceLayerId = -1;

        /// <summary>本节点在父级里充当的角色（bg / fill / handle / …）。</summary>
        public string Role = "none";

        public ControlKind Kind = ControlKind.Rect;

        /// <summary>Slider / Scrollbar 的朝向。</summary>
        public PlanAxis Axis = PlanAxis.Horizontal;

        /// <summary>相对父节点左上角的矩形（PSD 像素，Y 向下）。</summary>
        public UiRect Rect = UiRect.Empty;

        /// <summary>非空时忽略 <see cref="Rect"/>，改用锚点布局。</summary>
        public PlanAnchor Anchor;

        public bool Active = true;
        public double Opacity = 1d;

        /// <summary>图层是否带裁剪（面板/视口要加遮罩组件）。</summary>
        public bool Clipping;

        /// <summary>是不是模板生成的辅助节点（排查用）。</summary>
        public bool IsTemplate;

        // ---- 视觉 ----
        public string SpriteId;
        public string SpriteFile;
        public UiBorder Border;
        public bool Sliced;

        public bool HasColor;
        public UiColor Color = UiColor.White;

        public UiTextInfo Text;
        public List<UiEffect> Effects;

        // ---- 复合控件 ----
        public PlanSpriteState State;
        public readonly Dictionary<string, PlanNode> Slots = new Dictionary<string, PlanNode>();

        public readonly List<PlanNode> Children = new List<PlanNode>();

        /// <summary>被吸收掉的契约节点（例如按钮的按下态贴图），不再生成 GameObject。</summary>
        public readonly List<string> ConsumedNodeIds = new List<string>();

        /// <summary>
        /// `refp` 引用的子预制体名。非空时这个节点会以预制体实例的形式出现，
        /// 不再按图层生成子对象。
        /// </summary>
        public string PrefabTarget;

        /// <summary>受管属性的指纹，重新导出时用来判断「这个节点要不要动」。</summary>
        public string SpecHash
        {
            get { return PlanSpec.Hash(this); }
        }

        public PlanNode Add(PlanNode child)
        {
            Children.Add(child);
            return child;
        }

        public PlanNode Slot(string slot, PlanNode child)
        {
            Slots[slot] = child;
            return this;
        }

        public IEnumerable<PlanNode> SelfAndDescendants()
        {
            yield return this;
            for (int i = 0; i < Children.Count; i++)
            {
                foreach (PlanNode item in Children[i].SelfAndDescendants())
                {
                    yield return item;
                }
            }
        }

        public int CountDescendants()
        {
            int count = 0;
            for (int i = 0; i < Children.Count; i++)
            {
                count += 1 + Children[i].CountDescendants();
            }

            return count;
        }

        public override string ToString()
        {
            return Name + " [" + Kind + "] " + Rect;
        }
    }
}
