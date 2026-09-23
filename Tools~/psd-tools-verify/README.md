# psd-tools 交叉校验

自研解析器的正确性靠「和成熟实现对照」来保证：同一份 PSD，一边走 `PsdDump --layers`，
一边走 psd-tools，两边的图层明细应当逐字段一致。

psd-tools **只是开发期对照工具**，不是插件的依赖，Unity 里不会用到它。

## 准备

```bash
python3 -m venv /tmp/psdenv
/tmp/psdenv/bin/pip install psd-tools
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
