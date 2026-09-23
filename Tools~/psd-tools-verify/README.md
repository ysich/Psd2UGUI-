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

## 当前样本上的结果（2026-09-24）

| 样本 | 图层像素 | 合成结果 | psd-tools 自身合成（参考） |
| --- | --- | --- | --- |
| `Psd2UguiForm.psd` | 39 个图层最大差值 0 | 最大差值 0 / 4002000 字节 | 最大差值 240（效果烘焙差异） |
| `Psd2UguiForm_UGUI.psd` | 39 个图层最大差值 0 | 最大差值 0 / 4002000 字节 | 最大差值 191（效果烘焙差异） |

样本里没有图层蒙版与裁剪层，蒙版与裁剪链的行为由 `Tools~/CoreTests/ImagingTests.cs` 里的构造用例覆盖。

