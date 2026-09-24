using Psd2Ugui.Core.Build;
using Xunit;

namespace Psd2Ugui.CoreTests
{
    public class PrefabNodeNamingTests
    {
        [Theory]
        [InlineData(ControlKind.Rect, "Container", "m_rect_Container")]
        [InlineData(ControlKind.Image, "Icon", "m_img_Icon")]
        [InlineData(ControlKind.RawImage, "Avatar", "m_rimg_Avatar")]
        [InlineData(ControlKind.Text, "Title", "m_text_Title")]
        [InlineData(ControlKind.TmpText, "Title", "m_tmp_Title")]
        [InlineData(ControlKind.Button, "Start", "m_btn_Start")]
        [InlineData(ControlKind.Toggle, "Sound", "m_toggle_Sound")]
        [InlineData(ControlKind.ToggleGroup, "Tab", "m_group_Tab")]
        [InlineData(ControlKind.Grid, "Items", "m_grid_Items")]
        [InlineData(ControlKind.Slider, "Volume", "m_slider_Volume")]
        [InlineData(ControlKind.Scrollbar, "Vert", "m_scrollBar_Vert")]
        [InlineData(ControlKind.ScrollView, "List", "m_scroll_List")]
        [InlineData(ControlKind.Dropdown, "Select", "m_dropdown_Select")]
        [InlineData(ControlKind.InputField, "Name", "m_input_Name")]
        public void 按控件类型使用绑定前缀(ControlKind kind, string name, string expected)
        {
            Assert.Equal(expected, PrefabNodeNaming.NameFor(name, kind));
        }

        [Fact]
        public void 已有前缀不会重复并会按新类型规范化()
        {
            Assert.Equal("m_img_Icon", PrefabNodeNaming.NameFor("m_img_Icon", ControlKind.Image));
            Assert.Equal("m_img_Icon", PrefabNodeNaming.NameFor("m_btn_Icon", ControlKind.Image));
        }

        [Fact]
        public void 节点名中的空白和标点转为合法后缀()
        {
            Assert.Equal("m_img_Item_Background", PrefabNodeNaming.NameFor("Item Background", ControlKind.Image));
            Assert.Equal("m_img_Icon", PrefabNodeNaming.NameFor("Icon!", ControlKind.Image));
        }

        [Fact]
        public void 应用命名时保留根节点名并递归处理模板节点()
        {
            var root = new PlanNode { Name = "Login" };
            root.Add(new PlanNode { Name = "Icon", Kind = ControlKind.Image });
            var panel = root.Add(new PlanNode { Name = "Panel", Kind = ControlKind.Rect });
            panel.Add(new PlanNode { Name = "Title", Kind = ControlKind.Text });

            PrefabNodeNaming.Apply(root);

            Assert.Equal("Login", root.Name);
            Assert.Equal("m_img_Icon", root.Children[0].Name);
            Assert.Equal("m_rect_Panel", root.Children[1].Name);
            Assert.Equal("m_text_Title", root.Children[1].Children[0].Name);
        }

        [Fact]
        public void 没有TMP时文本回退到Text前缀()
        {
            var root = new PlanNode { Name = "Login" };
            root.Add(new PlanNode { Name = "Title", Kind = ControlKind.TmpText });

            PrefabNodeNaming.Apply(root, false);

            Assert.Equal("m_text_Title", root.Children[0].Name);
        }
    }
}
