using System.Collections.Generic;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    /// <summary>
    /// 增量更新的判断逻辑：指纹、层级键、以及「计划 vs 现状」该做什么动作。
    /// 这一层不碰 Unity，规则在这里钉死，编辑器侧只负责照做。
    /// </summary>
    public class PrefabMergeTests
    {
        [Fact]
        public void 同样的计划指纹一样()
        {
            PlanNode first = Node("n1", "Bg", ControlKind.Image, 1, 2, 30, 40);
            PlanNode second = Node("n1", "Bg", ControlKind.Image, 1, 2, 30, 40);

            Assert.Equal(PlanSpec.Hash(first), PlanSpec.Hash(second));
            Assert.NotEqual(string.Empty, PlanSpec.Hash(first));
        }

        [Theory]
        [InlineData("rect")]
        [InlineData("kind")]
        [InlineData("sprite")]
        [InlineData("color")]
        [InlineData("text")]
        [InlineData("active")]
        [InlineData("opacity")]
        public void 改一点指纹就变(string field)
        {
            PlanNode node = Node("n1", "Bg", ControlKind.Image, 1, 2, 30, 40);
            string before = PlanSpec.Hash(node);

            switch (field)
            {
                case "rect":
                    node.Rect = new UiRect(1d, 2d, 31d, 40d);
                    break;
                case "kind":
                    node.Kind = ControlKind.Text;
                    break;
                case "sprite":
                    node.SpriteId = "r1";
                    break;
                case "color":
                    node.HasColor = true;
                    node.Color = UiColor.FromBytes(255, 0, 0, 255);
                    break;
                case "text":
                    node.Text = new UiTextInfo { HasValue = true, Content = "标题", FontSize = 20d };
                    break;
                case "active":
                    node.Active = false;
                    break;
                case "opacity":
                    node.Opacity = 0.5d;
                    break;
            }

            Assert.NotEqual(before, PlanSpec.Hash(node));
        }

        [Fact]
        public void 布局里的极小抖动不影响指纹()
        {
            PlanNode first = Node("n1", "Bg", ControlKind.Image, 1.0000001, 2, 30, 40);
            PlanNode second = Node("n1", "Bg", ControlKind.Image, 1.0000002, 2, 30, 40);

            Assert.Equal(PlanSpec.Hash(first), PlanSpec.Hash(second));
        }

        [Fact]
        public void 键路径按稳定ID拼()
        {
            PlanNode node = Node("n1", "Bg", ControlKind.Image, 0, 0, 10, 10);
            PlanNode template = new PlanNode { Name = "Template", Kind = ControlKind.Rect, Active = true };

            Assert.Equal("n1", PlanKeys.Key(node));
            Assert.Equal("#Template", PlanKeys.Key(template));
            Assert.Equal("n1/n2", PlanKeys.Join("n1", "n2"));
            Assert.Equal("n1", PlanKeys.Join(null, "n1"));
        }

        [Fact]
        public void 没变就不动()
        {
            PlanNode root = Root();
            PlanNode child = Node("n1", "Bg", ControlKind.Image, 0, 0, 10, 10);
            root.Add(child);

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null),
                Match(child, "root/n1", PlanKeys.Key(root))
            }, root);

            Assert.Equal(MergeAction.Unchanged, operations[1].Action);
        }

        [Fact]
        public void 属性变了就刷新()
        {
            PlanNode root = Root();
            root.Add(Node("n1", "Bg", ControlKind.Image, 0, 0, 10, 10));

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null),
                new ExistingNode
                {
                    NodeId = "n1",
                    Key = "root/n1",
                    ParentKey = PlanKeys.Key(root),
                    Name = "Bg",
                    SpecHash = "旧指纹"
                }
            }, root);

            Assert.Equal(MergeAction.Update, operations[1].Action);
        }

        [Fact]
        public void 工程里没有就新建()
        {
            PlanNode root = Root();
            root.Add(Node("n1", "Bg", ControlKind.Image, 0, 0, 10, 10));

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null)
            }, root);

            Assert.Equal(MergeAction.Create, operations[1].Action);
        }

        [Fact]
        public void 换了父级要重挂()
        {
            PlanNode root = Root();
            PlanNode panel = Node("n2", "Panel", ControlKind.Panel, 0, 0, 50, 50);
            PlanNode bg = Node("n1", "Bg", ControlKind.Image, 0, 0, 10, 10);
            panel.Add(bg);
            root.Add(panel);

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null),
                Match(panel, "root/n2", PlanKeys.Key(root)),
                // 工程里 Bg 挂在根下，设计稿里它在 Panel 下
                Match(bg, "root/n1", PlanKeys.Key(root))
            }, root);

            MergeOp moved = Find(operations, "n1");
            Assert.Equal(MergeAction.Reparent, moved.Action);
            Assert.Equal("root/n2/n1", moved.Key);
            Assert.Equal("root/n2", moved.ParentKey);
        }

        [Fact]
        public void 设计稿删掉的节点要清理()
        {
            PlanNode root = Root();

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null),
                Existing("n9", "root/n9", PlanKeys.Key(root))
            }, root);

            Assert.Equal(MergeAction.Remove, Find(operations, "n9").Action);
        }

        [Fact]
        public void 带人工内容的节点保留不删()
        {
            PlanNode root = Root();

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null),
                new ExistingNode
                {
                    NodeId = "n9",
                    Key = "root/n9",
                    ParentKey = PlanKeys.Key(root),
                    Name = "Old",
                    HasUserContent = true
                }
            }, root);

            Assert.Equal(MergeAction.Keep, Find(operations, "n9").Action);
        }

        [Fact]
        public void 没标记的对象始终不碰()
        {
            PlanNode root = Root();

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null),
                new ExistingNode
                {
                    NodeId = "n9",
                    Key = "root/n9",
                    ParentKey = PlanKeys.Key(root),
                    Name = "人手复制出来的",
                    Generated = false
                }
            }, root);

            // 只有根节点那一条，人手复制出来的对象完全没被写进操作里
            Assert.Single(operations);
            Assert.Equal(MergeAction.Unchanged, operations[0].Action);
        }

        [Fact]
        public void 模板节点按键路径认领()
        {
            PlanNode root = Root();
            PlanNode view = new PlanNode
            {
                Name = "Viewport",
                Kind = ControlKind.Rect,
                IsTemplate = true,
                Active = true,
                Rect = new UiRect(0d, 0d, 10d, 10d)
            };
            root.Add(view);

            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                Match(root, PlanKeys.Key(root), null),
                Match(view, PlanKeys.Key(root) + "/#Viewport", PlanKeys.Key(root))
            }, root);

            MergeOp viewport = Find(operations, "#Viewport");
            Assert.Equal(MergeAction.Unchanged, viewport.Action);
        }

        [Fact]
        public void 根节点按计划键认领()
        {
            PlanNode root = Root();

            // 工程里根标记的 ID 和设计稿对不上（换了根图层），
            // 只要键路径对得上就还是同一个根，不该整棵重建
            List<MergeOp> operations = PrefabMerge.Diff(new List<ExistingNode>
            {
                new ExistingNode
                {
                    NodeId = "别的ID",
                    Key = PlanKeys.Key(root),
                    Name = root.Name,
                    SpecHash = PlanSpec.Hash(root)
                }
            }, root);

            Assert.Equal(MergeAction.Unchanged, operations[0].Action);
            Assert.Null(operations[0].ParentKey);
        }

        private static MergeOp Find(List<MergeOp> operations, string keyOrId)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                if (operations[i].NodeId == keyOrId || operations[i].Key == keyOrId ||
                    operations[i].Key.EndsWith("/" + keyOrId))
                {
                    return operations[i];
                }
            }

            return null;
        }

        /// <summary>和计划完全一致的现状条目（指纹也一样）。</summary>
        private static ExistingNode Match(PlanNode plan, string key, string parentKey)
        {
            return new ExistingNode
            {
                NodeId = plan.SourceNodeId ?? string.Empty,
                Key = key,
                ParentKey = parentKey,
                Name = plan.Name,
                SpecHash = PlanSpec.Hash(plan)
            };
        }

        private static ExistingNode Existing(string nodeId, string key, string parentKey)
        {
            return new ExistingNode
            {
                NodeId = nodeId ?? string.Empty,
                Key = key,
                ParentKey = parentKey,
                Name = key,
                SpecHash = string.Empty
            };
        }

        private static PlanNode Root()
        {
            return new PlanNode
            {
                Name = "Test",
                SourceNodeId = "root",
                Kind = ControlKind.Rect,
                Rect = new UiRect(0d, 0d, 100d, 200d),
                Active = true
            };
        }

        private static PlanNode Node(string id, string name, ControlKind kind, double x, double y, double width,
            double height)
        {
            return new PlanNode
            {
                Name = name,
                SourceNodeId = id,
                Kind = kind,
                Rect = new UiRect(x, y, width, height),
                Active = true
            };
        }
    }
}
