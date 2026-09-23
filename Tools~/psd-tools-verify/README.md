# psd-tools 交叉校验

自研解析器的正确性靠「和成熟实现对照」来保证：同一份 PSD，一边走 `PsdDump --layers`，
一边走 psd-tools，两边的图层明细应当逐字段一致。

psd-tools **只是开发期对照工具**，不是插件的依赖，Unity 里不会用到它。

## 准备

```bash
python3 -m venv /tmp/psdenv
/tmp/psdenv/bin/pip install psd-tools numpy

# 只有「让 psd-tools 自己合成整图」才需要这三个，图层像素对照不需要
/tmp/psdenv/bin/pip install scipy aggdraw scikit-image
```


## 用法

```bash
PSD="path/to/sample.psd"

# 对照组：psd-tools 的结果
/tmp/psdenv/bin/python Tools~/psd-tools-verify/dump_reference.py "$PSD" /tmp/reference.json

# 被测组：自研解析器的结果
dotnet run --project "Tools~/PsdDump" -- "$PSD" --layers /tmp/actual.json

# 逐字段比较，退出码非 0 表示存在差异
python3 Tools~/psd-tools-verify/compare.py /tmp/reference.json /tmp/actual.json
```

## 对照范围

- 图层名、`lyid`、矩形、不透明度、可见性、裁剪标记、`lsct` 分组类型
- 文本层的文字、字体、字号、颜色（ARGB）、对齐
- 图层效果的种类（`lfx2`，只统计 `enab` 与 `present` 同时为真的条目）
- 纯色填充层的颜色（`SoCo`）

`lsct == 3` 的收尾标记在两边都会被过滤掉，它只负责给分组开括号，不产生节点。

## 像素对照

结构对上了还要确认“像素也是对的”。两边各自把图层栅格化成裸 RGBA 字节，再逐字节比较：

```bash
PSD="path/to/sample.psd"

# 对照组：psd-tools 的图层像素 + 元信息（顺序、矩形、不透明度、可见性）
/tmp/psdenv/bin/python Tools~/psd-tools-verify/dump_pixels.py "$PSD" /tmp/px_ref

# 被测组：自研栅格化结果，外加整份文档的合成图
dotnet run --project "Tools~/PsdDump" -- "$PSD" --pixels /tmp/px_ours --composite /tmp/px_ours/composite.rgba

# 比较（需要用装了 numpy 的解释器）
/tmp/psdenv/bin/python Tools~/psd-tools-verify/compare_pixels.py /tmp/px_ref /tmp/px_ours
```

比较分三类：

1. **图层元信息**：图层顺序、矩形、不透明度、可见性逐项对照。
2. **图层像素**：每个图层 RGBA 逐字节对照，默认容差 ±2（现在实测差值为 0）。
3. **合成结果**：用 reference 侧的图层像素与元信息在 numpy 里独立重算一遍合成图，
   与 `PsdDump --composite` 的结果对照。这一项同时验证了图层顺序、不透明度与 alpha 混合公式。

`psd-tools` 自己合成的整图会**烘焙图层效果**（投影、描边、发光、渐变叠加），
我们导出的是“干净图层 + 效果单独成节点”，所以那份结果只作为参考信息打印，不计入失败。

## Sprite 与九宫对照

导出侧（`PsdDump --sprites`）会把 Image / RawImage 节点写成 PNG，并附上九宫检测结果。
再让 Pillow（独立的 PNG 解码器）把文件读回来，按九宫语义还原成原尺寸，与 psd-tools 的图层像素逐像素比：

```bash
PSD="path/to/sample.psd"

dotnet run --project "Tools~/PsdDump" -- "$PSD" --sprites /tmp/sprites

/tmp/psdenv/bin/python Tools~/psd-tools-verify/dump_pixels.py "$PSD" /tmp/px_ref
/tmp/psdenv/bin/python Tools~/psd-tools-verify/check_sprites.py /tmp/sprites /tmp/px_ref
```

检查项：

1. PNG 能被 Pillow 解码，颜色模式是 RGBA，尺寸与 `index.json` 一致；
2. `Sprite + Border` 按原尺寸还原后与 psd-tools 的图层像素（去空后）**逐像素一致**——
   这一步同时验证了 PNG 编码、九宫最小化（把可拉伸区压到 1 像素）与边框语义；
3. 没有九宫的图按原尺寸导出，直接一一对应。

## 当前样本上的结果（2026-09-24）

| 样本 | 图层像素 | 合成结果 | psd-tools 自身合成（参考） |
| --- | --- | --- | --- |
| `Psd2UguiForm.psd` | 39 个图层最大差值 0 | 最大差值 0 / 4002000 字节 | 最大差值 240（效果烘焙差异） |
| `Psd2UguiForm_UGUI.psd` | 39 个图层最大差值 0 | 最大差值 0 / 4002000 字节 | 最大差值 191（效果烘焙差异） |

| 样本 | Sprite | 其中九宫 | 九宫还原误差 |
| --- | --- | --- | --- |
| `Psd2UguiForm.psd` | 14 个 | 8 个 | 全部 0 |
| `Psd2UguiForm_UGUI.psd` | 19 个 | 12 个 | 全部 0 |

九宫检测的结论也符合直觉：滚动条/按钮/输入框这类圆角图判出 5~20 像素边框，
文字图（`Logo`、`Mark`、`Arrow`）与渐变图（`SliderBg`、`SliderFill`）判定为不可九宫。

样本里没有图层蒙版与裁剪层，蒙版与裁剪链的行为由 `Tools~/CoreTests/ImagingTests.cs` 里的构造用例覆盖。

