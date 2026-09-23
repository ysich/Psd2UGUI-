# 图层命名与覆盖表

> 给美术和前端看的一页纸：怎么在 PSD 里写图层名，以及推断错了怎么改。

## 1. 命名约定

```text
<名字>.<标签>.<标签>...
```

- 第一个 `.` 之前是节点名，之后的每一段都是标签，顺序随意、大小写不敏感。
- 标签分三类：**控件类型**、**子节点角色**、**开关**。
- 例：`Arrow.img.dpdicon` → 节点名 `Arrow`，类型 Image，角色 Arrow。
- 名字里没有点号的图层不会被误判成标签，标签段不认识时会记一条 Info 诊断（不报错）。
- 中文名可以直接用：`标题.tmptxt`。

## 2. 类型标签

| 标签 | 类型 | 标签 | 类型 |
| --- | --- | --- | --- |
| `img` `image` `pic` `spr` | Image | `dpd` `dropdown` `dd` | Dropdown |
| `rimg` `raw` `tex` `texture` | RawImage | `tmpdpd` `tmpdropdown` | TmpDropdown |
| `txt` `text` `uguitext` | Text | `ipt` `input` `inputfield` `edit` | InputField |
| `tmptxt` `tmptext` `tmp` | TmpText | `tmpipt` `tmpinput` | TmpInputField |
| `bt` `btn` `button` | Button | `msk` `mask` | Mask |
| `tmpbt` `tmpbtn` `tmpbutton` | TmpButton | `col` `color` `fillcolor` | FillColor |
| `tg` `tgl` `toggle` | Toggle | `panel` `pnl` `container` | Panel |
| `tmptg` `tmptoggle` | TmpToggle | `list` `lst` | List |
| `tgg` `togglegroup` | ToggleGroup | `grid` `gd` | Grid |
| `sld` `slider` `sdr` | Slider | `sv` `scrollview` `scroll` | ScrollView |

## 3. 角色标签（复合控件内部的零件）

| 标签 | 角色 | 标签 | 角色 |
| --- | --- | --- | --- |
| `bg` `background` `back` | Background | `vpt` `viewport` `view` | Viewport |
| `onover` `over` `hover` `highlight` | Highlight | `hbar` `hscrollbar` | HorizontalScrollbar |
| `press` `pressed` `down` | Pressed | `hbarbg` `hscrollbarbg` | HorizontalScrollbarBackground |
| `select` `selected` `on` `check` | Selected | `vbar` `vscrollbar` | VerticalScrollbar |
| `disable` `disabled` `gray` | Disabled | `vbarbg` `vscrollbarbg` | VerticalScrollbarBackground |
| `bttxt` `bttext` `buttontext` | ButtonText | `tips` `ph` `placeholder` | Placeholder |
| `mark` `indicator` `point` | Mark | `ipttxt` `iptlb` `inputtext` | InputText |
| `label` `lbl` `caption` | Label | `dpdicon` `arrow` `triangle` | Arrow |
| `fill` `progress` | Fill | `template` `tpl` `item` | Template |
| `handle` `hd` `knob` `thumb` | Handle | `preview` `pv` | Preview |

## 4. 开关标签

| 标签 | 作用 |
| --- | --- |
| `sliced` `slice` `9s` `9` | 这个图层按九宫格导出（Step 5 自动检测边框，也可以再用覆盖表手填） |
| `ignore` `skip` | 整棵子树不导出（在 PSD 里保留草稿图层用） |
| `ref <名字>` | 复用已有的图片资源，不再导出像素（跨界面共用同一张图） |
| `refp <名字>` | 复用已有的子 Prefab（整块 UI 复用） |

`ref` / `refp` 写在最前面，例如 `ref ButtonBule`、`refp Dialog.bt`。

## 5. 覆盖表

推断改不动的地方，用一份 JSON 覆盖表把结果钉死（重新导出时始终生效）：

```json
{
  "nodes": {
    "Common/VbarBG": { "nineSlice": false },
    "list": { "type": "image", "name": "ListBg" },
    "#669": { "type": "tmp-text", "role": "label", "name": "PercentLabel" },
    "Buttons/BlackBt/ButtonBlack": { "role": "background" },
    "Draft": { "ignore": true },
    "Loose": { "parent": "Panel" },
    "Icon": { "resource": "common/Shared/IconShare" }
  }
}
```

| key 形式 | 匹配方式 |
| --- | --- |
| `名字` | 节点名（去掉标签后的名字）相等 |
| `#图层ID` | PSD 图层 ID（`lyid`），最稳，重名也不会错 |
| `父/子/孙` | `LayerPath` 相等，用来精确定位同名节点 |

| 字段 | 说明 |
| --- | --- |
| `type` | 契约类型名（`tmp-text`）或图层标签（`tmptxt`、`img`）都能写 |
| `role` | 契约角色名（`background`）或标签（`bg`）都能写 |
| `name` | 覆盖节点名（不影响 `LayerPath`，重命名不会打断增量匹配） |
| `ignore` | 跳过整棵子树 |
| `nineSlice` | 强制开启/关闭九宫 |
| `parent` | 改层级：把这个节点挪到指定节点下（`#ID`、路径或名字都行，造成循环会被拒绝并给警告） |
| `resource` | 手动指定复用的资源名，等价于图层名的 `ref` |

多条规则命中同一个节点时，**后面的覆盖前面的**，所以类型和角色可以分开写在两条里。
匹配不到的条目会在诊断里给出 `override.unmatched` 警告，避免写错名字却没人发现。

## 6. 没写标签时的推断顺序

1. 覆盖表（人工最终裁决）
2. 图层名标签
3. 文本层 → `TmpText`（或按选项降级成 `Text`）
4. 分组 → `Group`（开启 `GroupsAsPanel` 时是 `Panel`）
5. 纯色填充层 → `FillColor`
6. 名字关键词（`button`、`toggle`、`slider`、`icon`、`bg`、`title`…）
7. 有像素数据 → `Image`，否则 → `None`（纯容器）

写了**角色**标签的图层不会再去抢控件类型（`list.bg` 是列表的背景图，不是 List 控件）。
把推断关掉（`NodeBuildOptions.InferTypes = false`）就只剩覆盖表和标签说了算。
