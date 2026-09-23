using System.Collections.Generic;

namespace Psd2Ugui.Core.Contract
{
    /// <summary>文本图层信息。HasValue 为 false 时表示该节点不是文本。</summary>
    public sealed class UiTextInfo
    {
        public bool HasValue;
        public string Content = string.Empty;
        public double FontSize;
        public UiColor Color = UiColor.White;
        public string FontKey = string.Empty;
        public string FontName = string.Empty;
        public string Align = "center";
        public bool WordWrap = true;
        public double LineSpacing;
        public double Tracking;
        public bool PointText;

        public UiTextInfo Clone()
        {
            return new UiTextInfo
            {
                HasValue = HasValue,
                Content = Content,
                FontSize = FontSize,
                Color = Color,
                FontKey = FontKey,
                FontName = FontName,
                Align = Align,
                WordWrap = WordWrap,
                LineSpacing = LineSpacing,
                Tracking = Tracking,
                PointText = PointText
            };
        }
    }

    /// <summary>图层效果（描边、投影、发光、渐变、斜面等）。</summary>
    public sealed class UiEffect
    {
        public string Kind = string.Empty;
        public bool Enabled = true;
        public double Size;
        public double Distance;
        public double Angle;
        public double Choke;
        public UiColor Color = UiColor.White;
        public double Opacity = 1d;
        public UiColor SecondColor;
        public bool HasSecondColor;
        public string BlendMode = string.Empty;
        public string Style = string.Empty;
    }

    /// <summary>
    /// 契约节点：一个 PSD 图层或图层组对应的界面元素。
    /// 所有几何量都是文档像素坐标（左上角原点，Y 轴向下）。
    /// </summary>
    public sealed class UiNode
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string LayerPath = string.Empty;
        public int LayerId = -1;
        public UiElementType Type = UiElementType.None;
        public UiRole Role = UiRole.None;
        public UiRect Rect = UiRect.Empty;
        public bool Visible = true;
        public double Opacity = 1d;
        public bool Clipping;
        public string SectionKind = string.Empty;
        public string ResourceId;

        /// <summary>`ref` / `refp` 的复用目标名，空表示不是引用节点。</summary>
        public string ReferenceTarget;

        /// <summary>true 表示 `refp`（复用子 Prefab），false 表示 `ref`（复用图片）。</summary>
        public bool IsPrefabReference;

        public UiBorder Border;
        public UiColor Fill;
        public bool HasFill;
        public UiTextInfo Text;
        public List<UiEffect> Effects = new List<UiEffect>();
        public List<UiNode> Children = new List<UiNode>();
        public Dictionary<string, string> Tags = new Dictionary<string, string>();

        public UiNode AddChild(UiNode child)
        {
            if (child != null)
            {
                Children.Add(child);
            }

            return this;
        }

        public IEnumerable<UiNode> SelfAndDescendants()
        {
            yield return this;
            for (int i = 0; i < Children.Count; i++)
            {
                foreach (UiNode node in Children[i].SelfAndDescendants())
                {
                    yield return node;
                }
            }
        }

        public UiNode FindById(string id)
        {
            foreach (UiNode node in SelfAndDescendants())
            {
                if (node.Id == id)
                {
                    return node;
                }
            }

            return null;
        }

        public int CountDescendants()
        {
            int count = 0;
            foreach (UiNode ignored in SelfAndDescendants())
            {
                count++;
            }

            return count;
        }
    }
}
