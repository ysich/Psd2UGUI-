using System.Collections.Generic;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Xunit;
using Psd2Ugui.Testing;

namespace Psd2Ugui.CoreTests
{
    /// <summary>
    /// 预制体装配计划：类型映射、布局换算、复合控件的子件连线。
    /// 这一层不碰 Unity，所以能在这里把规则钉死。
    /// </summary>
    public class PrefabPlannerTests
    {
        private const string MarkerName = "</Layer group>";
        private int _nextId;

        [Fact]
        public void 根节点按画布尺寸生成()
        {
            UiDocument document = Doc(Node("Bg", UiElementType.Image, 0, 0, 100, 200));

            PlanNode root = PrefabPlanner.Build(document);

            Assert.Equal("Test", root.Name);
            Assert.Equal(new UiRect(0d, 0d, 100d, 200d), root.Rect);
            Assert.Equal(ControlKind.Rect, root.Kind);
            Assert.Equal("2", document.Stats["prefabNodes"]);
        }

        [Fact]
        public void 子节点位置相对父节点()
        {
            UiNode panel = Node("Panel", UiElementType.Panel, 10, 20, 60, 40);
            panel.AddChild(Node("Icon", UiElementType.Image, 30, 50, 16, 16));

            PlanNode root = PrefabPlanner.Build(Doc(panel));

            PlanNode planPanel = root.Children[0];
            Assert.Equal(new UiRect(10d, 20d, 60d, 40d), planPanel.Rect);
            Assert.Equal(new UiRect(20d, 30d, 16d, 16d), planPanel.Children[0].Rect);
        }

        [Theory]
        [InlineData(UiElementType.Panel, ControlKind.Panel)]
        [InlineData(UiElementType.Mask, ControlKind.Mask)]
        [InlineData(UiElementType.Image, ControlKind.Image)]
        [InlineData(UiElementType.RawImage, ControlKind.RawImage)]
        [InlineData(UiElementType.Text, ControlKind.Text)]
        [InlineData(UiElementType.TmpText, ControlKind.TmpText)]
        [InlineData(UiElementType.FillColor, ControlKind.FillColor)]
        [InlineData(UiElementType.Button, ControlKind.Button)]
        [InlineData(UiElementType.TmpButton, ControlKind.Button)]
        [InlineData(UiElementType.Toggle, ControlKind.Toggle)]
        [InlineData(UiElementType.TmpToggle, ControlKind.Toggle)]
        [InlineData(UiElementType.ToggleGroup, ControlKind.ToggleGroup)]
        [InlineData(UiElementType.Slider, ControlKind.Slider)]
        [InlineData(UiElementType.ScrollView, ControlKind.ScrollView)]
        [InlineData(UiElementType.Dropdown, ControlKind.Dropdown)]
        [InlineData(UiElementType.TmpDropdown, ControlKind.Dropdown)]
        [InlineData(UiElementType.InputField, ControlKind.InputField)]
        [InlineData(UiElementType.TmpInputField, ControlKind.InputField)]
        [InlineData(UiElementType.Grid, ControlKind.Grid)]
        public void 契约类型映射成控件(UiElementType type, ControlKind expected)
        {
            UiDocument document = Doc(Node("X", type, 0, 0, 10, 10));

            Assert.Equal(expected, PrefabPlanner.Build(document).Children[0].Kind);
        }

        [Fact]
        public void 没有类型但有贴图的图层当图片()
        {
            UiDocument document = Doc(Node("Untagged", UiElementType.None, 0, 0, 10, 10));

            Assert.Equal(ControlKind.Rect, PrefabPlanner.Build(document).Children[0].Kind);
        }

        [Fact]
        public void 图片节点带上贴图与九宫()
        {
            UiNode image = Node("Frame", UiElementType.Image, 0, 0, 40, 20);
            UiDocument document = Doc(image);
            Sprite(document, image, "Frame", 23, 13, 10);

            PlanNode plan = PrefabPlanner.Build(document).Children[0];

            Assert.True(plan.Sliced);
            Assert.Equal("Frame.png", plan.SpriteFile);
            Assert.Equal(10, plan.Border.Left);
        }

        [Fact]
        public void 纯色块带颜色但不带动图()
        {
            UiNode fill = Node("Tint", UiElementType.FillColor, 0, 0, 10, 10);
            fill.HasFill = true;
            fill.Fill = UiColor.FromBytes(10, 20, 30, 255);

            PlanNode plan = PrefabPlanner.Build(Doc(fill)).Children[0];

            Assert.Equal(ControlKind.FillColor, plan.Kind);
            Assert.True(plan.HasColor);
            Assert.Equal(10d / 255d, plan.Color.R, 6);
            Assert.Null(plan.SpriteId);
        }

        [Fact]
        public void 隐藏节点标记为不激活()
        {
            UiNode hidden = Node("Hidden", UiElementType.Image, 0, 0, 10, 10);
            hidden.Visible = false;

            Assert.False(PrefabPlanner.Build(Doc(hidden)).Children[0].Active);
        }

        [Fact]
        public void 贴图裁过透明边时图片按内容框摆()
        {
            UiNode image = Node("Icon", UiElementType.Image, 10, 20, 30, 40);
            image.ContentRect = new UiRect(2d, 3d, 6d, 8d);

            PlanNode plan = PrefabPlanner.Build(Doc(image)).Children[0];

            // 节点矩形不动：子节点按它定位、状态图也铺在它上面
            Assert.Equal(new UiRect(10d, 20d, 30d, 40d), plan.Rect);
            // 图自己收进内容框，左上角偏出的是被裁掉的那几条边
            Assert.True(plan.DrawRect.HasValue);
            Assert.Equal(new UiRect(12d, 23d, 6d, 8d), plan.DrawRect.Value);
        }

        [Fact]
        public void 内容框盖满矩形或有子节点时不动矩形()
        {
            UiNode covered = Node("Icon", UiElementType.Image, 10, 20, 30, 40);
            covered.ContentRect = new UiRect(0d, 0d, 30d, 40d);

            UiNode container = Node("Panel", UiElementType.Panel, 0, 0, 30, 40);
            container.ContentRect = new UiRect(2d, 3d, 6d, 8d);
            container.AddChild(Node("Inside", UiElementType.Image, 4, 4, 8, 8));

            PlanNode plan = PrefabPlanner.Build(Doc(covered, container));

            // 一条边都没裁（内容框与矩形一样大）时不必收
            Assert.Null(plan.Children[0].DrawRect);
            // 有子节点的容器：矩形是子节点定位的参照，不能收
            Assert.Null(plan.Children[1].DrawRect);
        }

        [Fact]
        public void 按钮把四态贴图吸收进状态机()
        {
            UiNode button = Node("BlueBt", UiElementType.Button, 0, 0, 100, 40);
            UiNode background = Node("ButtonBule", UiElementType.Image, 0, 0, 100, 40, UiRole.Background);
            UiNode label = Node("ButtonTxt", UiElementType.TmpText, 10, 8, 80, 24, UiRole.ButtonText);
            UiNode hover = Node("Hover", UiElementType.Image, 0, 0, 100, 40, UiRole.Highlight);
            UiNode down = Node("Down", UiElementType.Image, 0, 0, 100, 40, UiRole.Pressed);
            button.AddChild(background).AddChild(label).AddChild(hover).AddChild(down);

            UiDocument document = Doc(button);
            Sprite(document, background, "ButtonBule", 100, 40);
            Sprite(document, hover, "Hover", 100, 40);
            Sprite(document, down, "Down", 100, 40);

            PlanNode plan = PrefabPlanner.Build(document).Children[0];

            Assert.Equal(ControlKind.Button, plan.Kind);
            Assert.Same(plan.Children[0], plan.Slots[PrefabPlanner.SlotTarget]);
            Assert.Same(plan.Children[1], plan.Children[1]);
            Assert.Equal("ButtonBule.png", plan.State.NormalFile);
            Assert.Equal("Hover.png", plan.State.HighlightedFile);
            Assert.Equal("Down.png", plan.State.PressedFile);
            // 悬停/按下只是颜色或贴图开关，不再生成 GameObject
            Assert.Equal(2, plan.Children.Count);
            Assert.Equal(2, plan.ConsumedNodeIds.Count);
            Assert.Equal("2", document.Stats["prefabConsumed"]);
        }

        [Fact]
        public void 没有交互态贴图的按钮不算有状态()
        {
            UiNode button = Node("BlackBt", UiElementType.Button, 0, 0, 100, 40);
            UiNode background = Node("Bg", UiElementType.Image, 0, 0, 100, 40, UiRole.Background);
            button.AddChild(background);

            UiDocument document = Doc(button);
            Sprite(document, background, "Bg", 100, 40);

            PlanNode plan = PrefabPlanner.Build(document).Children[0];

            Assert.True(plan.State.IsEmpty);
            Assert.Equal("Bg.png", plan.State.NormalFile);
        }

        [Fact]
        public void 开关连上底图与勾选图()
        {
            UiNode toggle = Node("Check", UiElementType.Toggle, 0, 0, 30, 30);
            UiNode background = Node("ToggleBg", UiElementType.Image, 0, 0, 30, 30, UiRole.Background);
            UiNode mark = Node("Mark", UiElementType.Image, 8, 8, 14, 14, UiRole.Mark);
            toggle.AddChild(background).AddChild(mark);

            UiDocument document = Doc(toggle);
            Sprite(document, background, "ToggleBg", 30, 30);
            Sprite(document, mark, "Mark", 14, 14);

            PlanNode plan = PrefabPlanner.Build(document).Children[0];

            Assert.Same(plan.Children[0], plan.Slots[PrefabPlanner.SlotTarget]);
            Assert.Same(plan.Children[1], plan.Slots[PrefabPlanner.SlotCheckmark]);
            Assert.DoesNotContain(document.Diagnostics, item => item.Code == "prefab.toggle-checkmark-missing");
        }

        [Fact]
        public void 开关缺少勾选图时告警()
        {
            UiNode toggle = Node("Check", UiElementType.Toggle, 0, 0, 30, 30);
            UiNode background = Node("ToggleBg", UiElementType.Image, 0, 0, 30, 30, UiRole.Background);
            toggle.AddChild(background);

            UiDocument document = Doc(toggle);
            Sprite(document, background, "ToggleBg", 30, 30);
            PrefabPlanner.Build(document);

            Assert.Contains(document.Diagnostics, item => item.Code == "prefab.toggle-checkmark-missing");
        }

        [Fact]
        public void 滑条连上填充与滑块并判出方向()
        {
            UiNode slider = Node("Row", UiElementType.Slider, 0, 0, 200, 30);
            UiNode fill = Node("Fill", UiElementType.FillColor, 0, 10, 100, 10, UiRole.Fill);
            UiNode handle = Node("Knob", UiElementType.Image, 90, 5, 20, 20, UiRole.Handle);
            slider.AddChild(fill).AddChild(handle);

            UiDocument document = Doc(slider);
            Sprite(document, handle, "Knob", 20, 20);

            PlanNode plan = PrefabPlanner.Build(document).Children[0];

            Assert.Equal(PlanAxis.Horizontal, plan.Axis);
            Assert.Same(plan.Children[0], plan.Slots[PrefabPlanner.SlotFill]);
            Assert.Same(plan.Children[1], plan.Slots[PrefabPlanner.SlotHandle]);
        }

        [Fact]
        public void 竖排滑条判成纵向()
        {
            UiNode slider = Node("Row", UiElementType.Slider, 0, 0, 30, 200);
            slider.AddChild(Node("Fill", UiElementType.FillColor, 10, 0, 10, 100, UiRole.Fill));

            UiDocument document = Doc(slider);
            Assert.Equal(PlanAxis.Vertical, PrefabPlanner.Build(document).Children[0].Axis);
        }

        [Fact]
        public void 滚动条角色映射成滚动条控件()
        {
            UiNode bar = Node("VbarFill", UiElementType.None, 0, 0, 20, 100, UiRole.VerticalScrollbar);
            UiNode handle = Node("Knob", UiElementType.Image, 0, 0, 18, 30, UiRole.Handle);
            bar.AddChild(handle);

            UiDocument document = Doc(bar);
            Sprite(document, handle, "Knob", 18, 30);

            PlanNode plan = PrefabPlanner.Build(document).Children[0];

            Assert.Equal(ControlKind.Scrollbar, plan.Kind);
            Assert.Equal(PlanAxis.Vertical, plan.Axis);
            Assert.Same(plan.Children[0], plan.Slots[PrefabPlanner.SlotHandle]);
        }

        [Fact]
        public void 滚动视图把内容挂到Content下()
        {
            UiNode scroll = Node("ScrollView", UiElementType.ScrollView, 0, 0, 400, 300);
            UiNode viewport = Node("Viewport", UiElementType.Panel, 0, 0, 400, 300, UiRole.Viewport);
            UiNode bar = Node("Vbar", UiElementType.None, 380, 0, 20, 300, UiRole.VerticalScrollbar);
            UiNode row = Node("Row", UiElementType.Image, 0, 0, 400, 60);
            scroll.AddChild(viewport).AddChild(bar).AddChild(row);

            UiDocument document = Doc(scroll);
            Sprite(document, row, "Row", 400, 60);

            PlanNode plan = PrefabPlanner.Build(document).Children[0];
            PlanNode content = plan.Slots[PrefabPlanner.SlotContent];

            Assert.Same(plan.Slots[PrefabPlanner.SlotViewport], plan.Children[0]);
            Assert.Same(plan.Slots[PrefabPlanner.SlotVerticalScrollbar], plan.Children[1]);
            Assert.Equal("m_rect_Content", content.Name);
            Assert.True(content.IsTemplate);
            Assert.Single(content.Children);
            Assert.Equal("m_img_Row", content.Children[0].Name);
            Assert.NotNull(content.Anchor);
        }

        [Fact]
        public void 滚动视图没有视口图层时自动补一个()
        {
            UiNode scroll = Node("ScrollView", UiElementType.ScrollView, 0, 0, 400, 300);
            scroll.AddChild(Node("Row", UiElementType.Image, 0, 0, 400, 60));

            UiDocument document = Doc(scroll);
            PrefabPlanner.Build(document);

            Assert.Contains(document.Diagnostics, item => item.Code == "prefab.scrollview-viewport-created");
            Assert.Contains(document.Diagnostics, item => item.Code == "prefab.scrollview-no-scrollbar");
        }

        [Fact]
        public void 输入框的文本槽位改成老Text组件()
        {
            UiNode field = Node("Edit", UiElementType.InputField, 0, 0, 200, 40);
            UiNode text = Node("Value", UiElementType.TmpText, 10, 8, 180, 24, UiRole.InputText);
            UiNode placeholder = Node("Tips", UiElementType.TmpText, 10, 8, 180, 24, UiRole.Placeholder);
            field.AddChild(text).AddChild(placeholder);

            UiDocument document = Doc(field);
            PlanNode plan = PrefabPlanner.Build(document).Children[0];

            Assert.Same(plan.Children[0], plan.Slots[PrefabPlanner.SlotTextComponent]);
            Assert.Same(plan.Children[1], plan.Slots[PrefabPlanner.SlotPlaceholder]);
            Assert.Equal(ControlKind.Text, plan.Children[0].Kind);
            Assert.Contains(document.Diagnostics, item => item.Code == "prefab.input-text-legacy");
        }

        [Fact]
        public void 下拉框补齐标准模板()
        {
            UiNode dropdown = Node("Kind", UiElementType.Dropdown, 0, 0, 200, 40);
            UiNode caption = Node("Label", UiElementType.TmpText, 10, 8, 160, 24, UiRole.ButtonText);
            dropdown.AddChild(caption);

            UiDocument document = Doc(dropdown);
            PlanNode plan = PrefabPlanner.Build(document).Children[0];
            PlanNode template = plan.Slots[PrefabPlanner.SlotTemplate];

            Assert.NotNull(template);
            Assert.False(template.Active);
            Assert.Equal(ControlKind.Text, plan.Children[0].Kind);
            Assert.Same(plan.Children[0], plan.Slots[PrefabPlanner.SlotCaptionText]);

            PlanNode viewport = template.Slots[PrefabPlanner.SlotViewport];
            PlanNode content = template.Slots[PrefabPlanner.SlotContent];
            Assert.Same(viewport, template.Children[0]);
            Assert.Same(content, viewport.Children[0]);
            Assert.Equal("m_toggle_Item", content.Children[0].Name);
            Assert.Equal(ControlKind.Toggle, content.Children[0].Kind);
            Assert.NotNull(plan.Slots[PrefabPlanner.SlotItemText]);
            Assert.Equal(ControlKind.Text, plan.Slots[PrefabPlanner.SlotItemText].Kind);
            Assert.Contains(document.Diagnostics, item => item.Code == "prefab.dropdown-template");
        }

        [Fact]
        public void 没跑导出计划时提示图片会变白块()
        {
            UiDocument document = Doc(Node("Icon", UiElementType.Image, 0, 0, 10, 10));
            PrefabPlanner.Build(document);

            Assert.Contains(document.Diagnostics, item => item.Code == "prefab.export-not-run");
        }

        [Fact]
        public void 导出计划跑过之后不再提示()
        {
            UiNode icon = Node("Icon", UiElementType.Image, 0, 0, 10, 10);
            UiDocument document = Doc(icon);
            Sprite(document, icon, "Icon", 10, 10);

            PrefabPlanner.Build(document);

            Assert.DoesNotContain(document.Diagnostics, item => item.Code == "prefab.export-not-run");
        }

        [Fact]
        public void 空文档返回空计划()
        {
            Assert.Null(PrefabPlanner.Build(null));
            Assert.Null(PrefabPlanner.Build(new UiDocument()));
        }

        [Fact]
        public void 相对矩形换算()
        {
            Assert.Equal(new UiRect(10d, 20d, 5d, 6d),
                PrefabPlanner.Relative(new UiRect(15d, 25d, 5d, 6d), new UiRect(5d, 5d, 100d, 100d)));
            Assert.Equal(new UiRect(15d, 25d, 5d, 6d),
                PrefabPlanner.Relative(new UiRect(15d, 25d, 5d, 6d), null));
        }

        [Fact]
        public void 真实样本能出一套完整计划()
        {
            var builder = new PsdFixtureBuilder { Width = 200, Height = 100 };
            Group(builder);
            Layer(builder, "ButtonTxt", 10, 10, 60, 20);
            Layer(builder, "ButtonBlack", 0, 0, 80, 40);
            EndGroup(builder, "BlackBt.btn");

            PsdFile file = PsdParser.Read(builder.Build());
            UiDocument document = NodeBuilder.Build(file);
            ExportPlan export = ExportPlanner.Build(file, document);
            PlanNode plan = PrefabPlanner.Build(document);

            Assert.Equal(2, export.Sprites.Count);
            Assert.Equal(ControlKind.Button, plan.Children[0].Kind);
            Assert.Equal(2, plan.Children[0].Children.Count);
            // 根 + 按钮组 + 文字 + 底图
            Assert.Equal("4", document.Stats["prefabNodes"]);
            Assert.Empty(document.Diagnostics.FindAll(item => item.Severity == DiagnosticSeverity.Error));
        }

        private UiDocument Doc(params UiNode[] children)
        {
            var document = new UiDocument();
            document.Document.Name = "Test";
            document.Document.Width = 100;
            document.Document.Height = 200;
            document.Root = new UiNode
            {
                Id = "root",
                Name = "Test",
                Type = UiElementType.Group,
                Rect = new UiRect(0d, 0d, 100d, 200d)
            };
            for (int i = 0; i < children.Length; i++)
            {
                document.Root.AddChild(children[i]);
            }

            return document;
        }

        private UiNode Node(string name, UiElementType type, double x, double y, double width, double height,
            UiRole role = UiRole.None)
        {
            _nextId++;
            return new UiNode
            {
                Id = "n" + _nextId,
                Name = name,
                LayerPath = name,
                LayerId = _nextId,
                Type = type,
                Role = role,
                Rect = new UiRect(x, y, width, height)
            };
        }

        private static void Sprite(UiDocument document, UiNode node, string name, int width, int height,
            int border = 0)
        {
            string id = StableId.ResourceId(UiResourceKind.Sprite, node.LayerPath);
            node.ResourceId = id;
            document.Resources.Add(new UiResource
            {
                Id = id,
                Kind = UiResourceKind.Sprite,
                Name = name,
                Module = "common",
                FileName = name + ".png",
                ContentHash = StableId.Hash(name),
                Width = width,
                Height = height,
                Border = new UiBorder(border, border, border, border)
            });
        }

        private static LayerSpec Layer(PsdFixtureBuilder builder, string name, int x, int y, int width, int height)
        {
            LayerSpec layer = builder.AddLayer(name);
            layer.Left = x;
            layer.Top = y;
            layer.Right = x + width;
            layer.Bottom = y + height;
            int count = width * height;
            var channel = new byte[count];
            for (int i = 0; i < count; i++)
            {
                channel[i] = (byte)(20 + (i % 200));
            }

            var alpha = new byte[count];
            for (int i = 0; i < count; i++)
            {
                alpha[i] = 255;
            }

            layer.WithChannel(0, PsdCompression.Raw, channel)
                .WithChannel(1, PsdCompression.Raw, channel)
                .WithChannel(2, PsdCompression.Raw, channel)
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, alpha);
            return layer;
        }

        private static void Group(PsdFixtureBuilder builder)
        {
            LayerSpec marker = builder.AddLayer(MarkerName);
            marker.Right = 0;
            marker.Bottom = 0;
            marker.WithTag("lsct", Divider(3)).WithChannel(0, PsdCompression.Raw, new byte[] { 0 });
        }

        private static void EndGroup(PsdFixtureBuilder builder, string name)
        {
            LayerSpec group = builder.AddLayer(name);
            group.Right = 0;
            group.Bottom = 0;
            group.WithTag("lsct", Divider(1)).WithChannel(0, PsdCompression.Raw, new byte[] { 0 });
        }

        private static byte[] Divider(int value)
        {
            var payload = new List<byte>();
            PsdFixtureBuilder.WriteI32(payload, value);
            return payload.ToArray();
        }
    }
}
