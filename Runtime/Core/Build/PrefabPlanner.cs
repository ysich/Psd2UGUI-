using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;

namespace Psd2Ugui.Core.Build
{
    /// <summary>
    /// 把契约节点树翻译成「预制体装配计划」：每个节点要挂什么控件、放在哪、
    /// 复合控件的子件怎么连线、哪些图层会被吸收成状态图。
    ///
    /// 这一层不碰 Unity，所以控件映射规则与布局换算都能用 dotnet 断言；
    /// Editor 侧只负责照着计划创建 GameObject 与组件。
    /// </summary>
    public static class PrefabPlanner
    {
        /// <summary>复合控件子件的槽位名，Editor 侧按这些名字连线。</summary>
        public const string SlotTarget = "target";
        public const string SlotCheckmark = "checkmark";
        public const string SlotFill = "fill";
        public const string SlotHandle = "handle";
        public const string SlotViewport = "viewport";
        public const string SlotContent = "content";
        public const string SlotVerticalScrollbar = "verticalScrollbar";
        public const string SlotHorizontalScrollbar = "horizontalScrollbar";
        public const string SlotTemplate = "template";
        public const string SlotCaptionText = "captionText";
        public const string SlotItemText = "itemText";
        public const string SlotTextComponent = "textComponent";
        public const string SlotPlaceholder = "placeholder";

        public static PlanNode Build(UiDocument document, BuildOptions options = null)
        {
            if (document == null || document.Root == null)
            {
                return null;
            }

            options = options ?? new BuildOptions();
            var context = new Context(document, options);
            WarnUnboundResources(document);
            PlanNode root = Plan(context, document.Root, null);
            if (root != null)
            {
                root.Name = string.IsNullOrEmpty(document.Document.Name) ? "Root" : document.Document.Name;
                document.Stats["prefabNodes"] = Count(root).ToString();
                document.Stats["prefabConsumed"] = context.Consumed.ToString();
            }

            return root;
        }

        /// <summary>
        /// 装配依赖导出计划给出的「节点 → 贴图」绑定。
        /// 一个资源都没有却又有需要贴图的节点，说明导出计划还没跑过，
        /// 这时统一提示一次，免得生成出一堆白块还不知道为什么。
        /// </summary>
        private static void WarnUnboundResources(UiDocument document)
        {
            if (document.Resources.Count > 0)
            {
                return;
            }

            foreach (UiNode node in document.Nodes())
            {
                if (ExportPlanner.NeedsSprite(node))
                {
                    document.Report(DiagnosticSeverity.Info, "prefab.export-not-run",
                        "还没有导出任何贴图，图片节点会生成成白块；请先跑一次导出计划（ExportPlanner）");
                    return;
                }
            }
        }

        private sealed class Context
        {
            public Context(UiDocument document, BuildOptions options)
            {
                Document = document;
                Options = options;
                for (int i = 0; i < document.Resources.Count; i++)
                {
                    Resources[document.Resources[i].Id] = document.Resources[i];
                }
            }

            public readonly UiDocument Document;
            public readonly BuildOptions Options;
            public readonly Dictionary<string, UiResource> Resources = new Dictionary<string, UiResource>();
            public int Consumed;

            public UiResource Resource(string id)
            {
                UiResource resource;
                return !string.IsNullOrEmpty(id) && Resources.TryGetValue(id, out resource) ? resource : null;
            }
        }

        private static PlanNode Plan(Context context, UiNode node, UiRect? parentRect)
        {
            UiRole role = node.Role;
            var plan = new PlanNode
            {
                Name = string.IsNullOrEmpty(node.Name) ? "Node" : node.Name,
                SourceNodeId = node.Id,
                SourceLayerPath = node.LayerPath,
                SourceLayerId = node.LayerId,
                Role = role.ToContract(),
                Kind = KindFor(node, role),
                Rect = Relative(node.Rect, parentRect),
                Active = node.Visible,
                Opacity = node.Opacity,
                Clipping = node.Clipping,
                Text = node.Text,
                Effects = node.Effects
            };

            if (node.HasFill)
            {
                plan.HasColor = true;
                plan.Color = node.Fill;
            }

            ApplySprite(context, node, plan);
            PlanChildren(context, node, plan);
            return plan;
        }

        private static void PlanChildren(Context context, UiNode node, PlanNode plan)
        {
            switch (plan.Kind)
            {
                case ControlKind.Button:
                    PlanButton(context, node, plan);
                    return;
                case ControlKind.Toggle:
                    PlanToggle(context, node, plan);
                    return;
                case ControlKind.Slider:
                    PlanSlider(context, node, plan);
                    return;
                case ControlKind.Scrollbar:
                    PlanScrollbar(context, node, plan);
                    return;
                case ControlKind.ScrollView:
                    PlanScrollView(context, node, plan);
                    return;
                case ControlKind.Dropdown:
                    PlanDropdown(context, node, plan);
                    return;
                case ControlKind.InputField:
                    PlanInputField(context, node, plan);
                    return;
                default:
                    PlanPlainChildren(context, node, plan);
                    return;
            }
        }

        /// <summary>
        /// 按钮：底图当 <c>targetGraphic</c>，文字当子节点，
        /// 悬停/按下/选中/禁用四态贴图被吸收进 <c>spriteState</c>，不再生成对象。
        /// </summary>
        private static void PlanButton(Context context, UiNode node, PlanNode plan)
        {
            UiNode target = null;
            UiNode label = null;
            var consumed = new HashSet<UiNode>();

            for (int i = 0; i < node.Children.Count; i++)
            {
                UiNode child = node.Children[i];
                if (IsStateRole(child.Role))
                {
                    consumed.Add(child);
                    AbsorbState(context, plan, child);
                    continue;
                }

                if (label == null && IsTextLike(child))
                {
                    label = child;
                    continue;
                }

                if (target == null && (child.Role == UiRole.Background || HasSprite(context, child)))
                {
                    target = child;
                }
            }

            var planned = new Dictionary<UiNode, PlanNode>();
            for (int i = 0; i < node.Children.Count; i++)
            {
                UiNode child = node.Children[i];
                if (consumed.Contains(child))
                {
                    plan.ConsumedNodeIds.Add(child.Id);
                    context.Consumed++;
                    continue;
                }

                PlanNode childPlan = Plan(context, child, node.Rect);
                planned[child] = childPlan;
                plan.Add(childPlan);
            }

            if (plan.State == null)
            {
                plan.State = new PlanSpriteState();
            }

            UiResource normal = target == null ? null : context.Resource(target.ResourceId);
            if (!string.IsNullOrEmpty(plan.SpriteId))
            {
                plan.State.NormalId = plan.SpriteId;
                plan.State.NormalFile = plan.SpriteFile;
            }
            else if (normal != null)
            {
                plan.State.NormalId = normal.Id;
                plan.State.NormalFile = normal.FileName;
            }

            Slot(plan, planned, target, SlotTarget);
        }

        private static void PlanToggle(Context context, UiNode node, PlanNode plan)
        {
            UiNode target = FirstByRole(node, UiRole.Background) ?? FirstImage(context, node);
            UiNode mark = FirstByRole(node, UiRole.Mark) ?? FirstImage(context, node, target);

            var planned = PlanPlainChildren(context, node, plan);
            Slot(plan, planned, target, SlotTarget);
            Slot(plan, planned, mark, SlotCheckmark);

            if (mark == null)
            {
                context.Document.Report(DiagnosticSeverity.Warning, "prefab.toggle-checkmark-missing",
                    "开关没有勾选图（.mark）子件，运行时会看不到选中状态：" + plan.Name);
            }
        }

        private static void PlanSlider(Context context, UiNode node, PlanNode plan)
        {
            UiNode fill = FirstByRole(node, UiRole.Fill);
            UiNode handle = FirstByRole(node, UiRole.Handle);
            plan.Axis = node.Rect.Width >= node.Rect.Height ? PlanAxis.Horizontal : PlanAxis.Vertical;

            var planned = PlanPlainChildren(context, node, plan);
            Slot(plan, planned, fill, SlotFill);
            Slot(plan, planned, handle, SlotHandle);

            if (fill == null)
            {
                context.Document.Report(DiagnosticSeverity.Warning, "prefab.slider-fill-missing",
                    "滑条没有填充图（.fill）子件：" + plan.Name);
            }

            if (handle == null)
            {
                context.Document.Report(DiagnosticSeverity.Warning, "prefab.slider-handle-missing",
                    "滑条没有滑块图（.handle）子件：" + plan.Name);
            }
        }

        private static void PlanScrollbar(Context context, UiNode node, PlanNode plan)
        {
            plan.Axis = node.Role == UiRole.VerticalScrollbar ? PlanAxis.Vertical : PlanAxis.Horizontal;
            UiNode background = node.Role == UiRole.VerticalScrollbar
                ? FirstByRole(node, UiRole.VerticalScrollbarBackground)
                : FirstByRole(node, UiRole.HorizontalScrollbarBackground);
            UiNode handle = FirstByRole(node, UiRole.Handle) ?? FirstImage(context, node, background);

            var planned = PlanPlainChildren(context, node, plan);
            Slot(plan, planned, background, SlotTarget);
            Slot(plan, planned, handle, SlotHandle);

            if (handle == null)
            {
                context.Document.Report(DiagnosticSeverity.Warning, "prefab.scrollbar-handle-missing",
                    "滚动条没有滑块子件：" + plan.Name);
            }
        }

        /// <summary>
        /// 滚动视图：视口之外的内容整体挂到 Content 下，
        /// 这样 <c>ScrollRect</c> 拖动的才是真正的列表内容。
        /// </summary>
        private static void PlanScrollView(Context context, UiNode node, PlanNode plan)
        {
            UiNode viewport = FirstByRole(node, UiRole.Viewport);
            UiNode vertical = FirstByRole(node, UiRole.VerticalScrollbar);
            UiNode horizontal = FirstByRole(node, UiRole.HorizontalScrollbar);

            var planned = new Dictionary<UiNode, PlanNode>();
            var contentChildren = new List<UiNode>();
            for (int i = 0; i < node.Children.Count; i++)
            {
                UiNode child = node.Children[i];
                if (child == viewport || child == vertical || child == horizontal)
                {
                    PlanNode childPlan = Plan(context, child, node.Rect);
                    planned[child] = childPlan;
                    plan.Add(childPlan);
                    continue;
                }

                contentChildren.Add(child);
            }

            Slot(plan, planned, viewport, SlotViewport);
            Slot(plan, planned, vertical, SlotVerticalScrollbar);
            Slot(plan, planned, horizontal, SlotHorizontalScrollbar);

            PlanNode viewportPlan = planned.ContainsKey(viewport ?? node) ? planned[viewport] : null;
            UiRect viewportRect = viewport != null ? viewport.Rect : node.Rect;
            PlanNode content = viewport != null ? viewportPlan : null;
            if (content == null)
            {
                // 没有单独的视口图层时，就地造一个和滚动视图等大的视口
                content = new PlanNode
                {
                    Name = "Viewport",
                    Kind = ControlKind.Rect,
                    Rect = new UiRect(0d, 0d, node.Rect.Width, node.Rect.Height),
                    IsTemplate = true,
                    Role = "viewport",
                    Active = true
                };
                plan.Add(content);
                plan.Slot(SlotViewport, content);
                context.Document.Report(DiagnosticSeverity.Info, "prefab.scrollview-viewport-created",
                    "滚动视图没有视口图层，已按自身矩形生成一个：" + plan.Name);
            }

            var contentRoot = new PlanNode
            {
                Name = "Content",
                Kind = ControlKind.Rect,
                Rect = new UiRect(0d, 0d, viewportRect.Width, viewportRect.Height),
                Anchor = PlanAnchor.TopStretch(viewportRect.Height),
                IsTemplate = true,
                Role = "content",
                Active = true
            };
            content.Add(contentRoot);
            for (int i = 0; i < contentChildren.Count; i++)
            {
                PlanNode childPlan = Plan(context, contentChildren[i], viewportRect);
                contentRoot.Add(childPlan);
            }

            plan.Slot(SlotContent, contentRoot);

            if (vertical == null && horizontal == null)
            {
                context.Document.Report(DiagnosticSeverity.Info, "prefab.scrollview-no-scrollbar",
                    "滚动视图没有滚动条子件，只能靠拖动滚动：" + plan.Name);
            }
        }

        private static void PlanDropdown(Context context, UiNode node, PlanNode plan)
        {
            UiNode caption = FirstByRole(node, UiRole.ButtonText) ?? FirstText(node);
            var planned = PlanPlainChildren(context, node, plan);
            Slot(plan, planned, caption, SlotCaptionText);
            ForceLegacyText(context, planned, caption, "prefab.dropdown-caption-legacy",
                "uGUI Dropdown 的标题文字固定是 Text，已把 TMP 图层改成 Text：");

            // PSD 里常常已经把列表结构画好了（列表 ScrollView + 列表项 Toggle），
            // 能复用就复用，免得运行时出现「设计稿的列表 + 组件自己的列表」两份
            UiNode listChild = FirstByKind(node, UiElementType.ScrollView);
            UiNode itemChild = FirstByKind(node, UiElementType.Toggle);
            if (listChild != null && itemChild != null &&
                AdoptDropdownList(context, node, plan, planned, listChild, itemChild))
            {
                return;
            }

            // 没有可用结构时，按 uGUI 默认结构补一套
            var template = new PlanNode
            {
                Name = "Template",
                Kind = ControlKind.ScrollView,
                Rect = new UiRect(0d, 0d, node.Rect.Width, node.Rect.Height * 3d),
                IsTemplate = true,
                Active = false,
                Role = "template"
            };
            var viewport = new PlanNode
            {
                Name = "Viewport",
                Kind = ControlKind.Mask,
                Rect = new UiRect(0d, 0d, node.Rect.Width, node.Rect.Height * 3d),
                IsTemplate = true,
                Role = "viewport"
            };
            var content = new PlanNode
            {
                Name = "Content",
                Kind = ControlKind.Rect,
                Rect = new UiRect(0d, 0d, node.Rect.Width, node.Rect.Height),
                Anchor = PlanAnchor.TopStretch(node.Rect.Height),
                IsTemplate = true,
                Role = "content"
            };
            var item = new PlanNode
            {
                Name = "Item",
                Kind = ControlKind.Toggle,
                Rect = new UiRect(0d, 0d, node.Rect.Width, node.Rect.Height),
                Anchor = PlanAnchor.TopStretch(node.Rect.Height),
                IsTemplate = true,
                Role = "item"
            };
            var itemBackground = new PlanNode
            {
                Name = "Item Background",
                Kind = ControlKind.Image,
                Rect = new UiRect(0d, 0d, node.Rect.Width, node.Rect.Height),
                IsTemplate = true,
                Role = "bg"
            };
            var itemCheckmark = new PlanNode
            {
                Name = "Item Checkmark",
                Kind = ControlKind.Image,
                Rect = new UiRect(0d, 0d, node.Rect.Height, node.Rect.Height),
                IsTemplate = true,
                Role = "mark"
            };
            var itemLabel = new PlanNode
            {
                Name = "Item Label",
                Kind = ControlKind.Text,
                Rect = new UiRect(0d, 0d, node.Rect.Width, node.Rect.Height),
                IsTemplate = true,
                Role = "label",
                Text = caption != null ? caption.Text : NewText(node)
            };

            item.Add(itemBackground);
            item.Add(itemCheckmark);
            item.Add(itemLabel);
            item.Slot(SlotTarget, itemBackground);
            item.Slot(SlotCheckmark, itemCheckmark);
            content.Add(item);
            viewport.Add(content);
            template.Add(viewport);
            template.Slot(SlotViewport, viewport);
            template.Slot(SlotContent, content);
            plan.Add(template);
            plan.Slot(SlotTemplate, template);
            plan.Slot(SlotItemText, itemLabel);

            context.Document.Report(DiagnosticSeverity.Info, "prefab.dropdown-template",
                "下拉框按 uGUI 默认结构补齐了 Template（列表项文字沿用第一个文字图层）：" + plan.Name);
        }

        /// <summary>把设计稿里的列表与列表项接成 Dropdown 的 Template / Item。</summary>
        private static bool AdoptDropdownList(Context context, UiNode node, PlanNode plan,
            Dictionary<UiNode, PlanNode> planned, UiNode listChild, UiNode itemChild)
        {
            PlanNode template;
            PlanNode item;
            if (!planned.TryGetValue(listChild, out template) || !planned.TryGetValue(itemChild, out item))
            {
                return false;
            }

            PlanNode content = template.Slots.ContainsKey(SlotContent) ? template.Slots[SlotContent] : null;
            if (content == null)
            {
                return false;
            }

            // 列表项要挂在 Template 的 Content 下，uGUI 的 Dropdown 才找得到它
            plan.Children.Remove(item);
            UiRect baseRect = listChild.Rect;
            Shift(item, node.Rect.X - baseRect.X, node.Rect.Y - baseRect.Y);
            content.Add(item);

            UiNode label = FirstByRole(itemChild, UiRole.ButtonText) ?? FirstText(itemChild);
            PlanNode itemLabel;
            if (label != null && planned.TryGetValue(label, out itemLabel))
            {
                plan.Slot(SlotItemText, itemLabel);
            }

            template.Active = false;
            plan.Slot(SlotTemplate, template);
            context.Document.Report(DiagnosticSeverity.Info, "prefab.dropdown-adopt-list",
                "下拉框复用了设计稿里的列表结构（列表项已挂到 Template 的 Content 下）：" + plan.Name);
            return true;
        }

        private static void Shift(PlanNode node, double dx, double dy)
        {
            node.Rect = new UiRect(node.Rect.X + dx, node.Rect.Y + dy, node.Rect.Width, node.Rect.Height);
            for (int i = 0; i < node.Children.Count; i++)
            {
                Shift(node.Children[i], dx, dy);
            }
        }

        private static UiNode FirstByKind(UiNode node, UiElementType type)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (node.Children[i].Type == type)
                {
                    return node.Children[i];
                }
            }

            return null;
        }

        private static void PlanInputField(Context context, UiNode node, PlanNode plan)
        {
            UiNode text = FirstByRole(node, UiRole.InputText) ?? FirstText(node);
            UiNode placeholder = FirstByRole(node, UiRole.Placeholder);
            if (placeholder == null)
            {
                placeholder = FirstText(node, text);
            }

            var planned = PlanPlainChildren(context, node, plan);
            Slot(plan, planned, text, SlotTextComponent);
            Slot(plan, planned, placeholder, SlotPlaceholder);

            ForceLegacyText(context, planned, text, "prefab.input-text-legacy",
                "uGUI InputField 的文本组件固定是 Text，已把 TMP 图层改成 Text：");

            if (text == null)
            {
                context.Document.Report(DiagnosticSeverity.Warning, "prefab.input-text-missing",
                    "输入框没有文字图层（.ipttxt），运行时会看不到输入内容：" + plan.Name);
            }
        }

        /// <summary>
        /// uGUI 的 Dropdown / InputField 只认老 Text 组件，
        /// 所以这两处的文字节点在这里就把类型改掉，而不是留给 Editor 侧偷偷换组件。
        /// </summary>
        private static void ForceLegacyText(Context context, Dictionary<UiNode, PlanNode> planned, UiNode child,
            string code, string message)
        {
            PlanNode plan;
            if (child == null || !planned.TryGetValue(child, out plan) || plan.Kind != ControlKind.TmpText)
            {
                return;
            }

            plan.Kind = ControlKind.Text;
            context.Document.Report(DiagnosticSeverity.Info, code, message + plan.Name);
        }

        private static UiTextInfo NewText(UiNode node)
        {
            return new UiTextInfo { Content = node.Name, FontSize = Math.Max(12d, node.Rect.Height * 0.5d) };
        }

        /// <summary>把子节点按角色登记到槽位，同时把没生成节点的槽位留空。</summary>
        private static void Slot(PlanNode plan, Dictionary<UiNode, PlanNode> planned, UiNode child, string slot)
        {
            PlanNode node;
            if (child != null && planned.TryGetValue(child, out node))
            {
                plan.Slot(slot, node);
            }
        }

        private static Dictionary<UiNode, PlanNode> PlanPlainChildren(Context context, UiNode node, PlanNode plan)
        {
            var planned = new Dictionary<UiNode, PlanNode>();
            for (int i = 0; i < node.Children.Count; i++)
            {
                PlanNode child = Plan(context, node.Children[i], node.Rect);
                planned[node.Children[i]] = child;
                plan.Add(child);
            }

            return planned;
        }

        private static void ApplySprite(Context context, UiNode node, PlanNode plan)
        {
            UiResource resource = context.Resource(node.ResourceId);
            if (resource == null)
            {
                return;
            }

            plan.SpriteId = resource.Id;
            plan.SpriteFile = resource.FileName;
            plan.Border = resource.Border;
            plan.Sliced = resource.Border != null && !resource.Border.IsZero;
        }

        private static void AbsorbState(Context context, PlanNode plan, UiNode child)
        {
            if (plan.State == null)
            {
                plan.State = new PlanSpriteState();
            }

            UiResource resource = context.Resource(child.ResourceId);
            if (resource == null)
            {
                context.Document.Report(DiagnosticSeverity.Warning, "prefab.state-sprite-missing",
                    "状态图层没有导出贴图，按钮的交互态会沿用常态图：" + child.Name);
                return;
            }

            switch (child.Role)
            {
                case UiRole.Highlight:
                    plan.State.HighlightedId = resource.Id;
                    plan.State.HighlightedFile = resource.FileName;
                    break;
                case UiRole.Pressed:
                    plan.State.PressedId = resource.Id;
                    plan.State.PressedFile = resource.FileName;
                    break;
                case UiRole.Selected:
                    plan.State.SelectedId = resource.Id;
                    plan.State.SelectedFile = resource.FileName;
                    break;
                case UiRole.Disabled:
                    plan.State.DisabledId = resource.Id;
                    plan.State.DisabledFile = resource.FileName;
                    break;
            }
        }

        /// <summary>契约类型 + 角色 → 控件种类。</summary>
        private static ControlKind KindFor(UiNode node, UiRole role)
        {
            if (role == UiRole.VerticalScrollbar || role == UiRole.HorizontalScrollbar)
            {
                return ControlKind.Scrollbar;
            }

            switch (node.Type)
            {
                case UiElementType.Group:
                    return ControlKind.Rect;
                case UiElementType.Panel:
                    return ControlKind.Panel;
                case UiElementType.Mask:
                    return ControlKind.Mask;
                case UiElementType.Image:
                    return ControlKind.Image;
                case UiElementType.RawImage:
                    return ControlKind.RawImage;
                case UiElementType.Text:
                    return ControlKind.Text;
                case UiElementType.TmpText:
                    return ControlKind.TmpText;
                case UiElementType.FillColor:
                    return ControlKind.FillColor;
                case UiElementType.Button:
                    return ControlKind.Button;
                case UiElementType.Toggle:
                    return ControlKind.Toggle;
                case UiElementType.Slider:
                    return ControlKind.Slider;
                case UiElementType.ScrollView:
                    return ControlKind.ScrollView;
                case UiElementType.Dropdown:
                    return ControlKind.Dropdown;
                case UiElementType.InputField:
                    return ControlKind.InputField;
                case UiElementType.Ignore:
                    return ControlKind.Rect;
                default:
                    // 没有类型也没有标签的图层：有贴图就当图片，否则当容器
                    return string.IsNullOrEmpty(node.ResourceId) ? ControlKind.Rect : ControlKind.Image;
            }
        }

        private static bool IsStateRole(UiRole role)
        {
            return role == UiRole.Highlight || role == UiRole.Pressed || role == UiRole.Selected ||
                   role == UiRole.Disabled;
        }

        private static bool IsTextLike(UiNode node)
        {
            return node.Type == UiElementType.Text || node.Type == UiElementType.TmpText;
        }

        private static bool HasSprite(Context context, UiNode node)
        {
            return context.Resource(node.ResourceId) != null;
        }

        private static UiNode FirstByRole(UiNode node, UiRole role)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (node.Children[i].Role == role)
                {
                    return node.Children[i];
                }
            }

            return null;
        }

        private static UiNode FirstImage(Context context, UiNode node, UiNode skip = null)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                UiNode child = node.Children[i];
                if (child != skip && !IsTextLike(child) && HasSprite(context, child))
                {
                    return child;
                }
            }

            return null;
        }

        private static UiNode FirstText(UiNode node, UiNode skip = null)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                UiNode child = node.Children[i];
                if (child != skip && child.Text != null && child.Text.HasValue)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>把绝对矩形换算成「相对父节点左上角」的矩形。</summary>
        public static UiRect Relative(UiRect rect, UiRect? parent)
        {
            if (!parent.HasValue)
            {
                return rect;
            }

            return new UiRect(rect.X - parent.Value.X, rect.Y - parent.Value.Y, rect.Width, rect.Height);
        }

        private static int Count(PlanNode root)
        {
            return 1 + root.CountDescendants();
        }
    }
}
