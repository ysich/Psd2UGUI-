# 中间契约格式

> 契约 = **PSD 怎么被理解**这件事的可读快照。解析层写它，装配层读它，
> 出问题时它是唯一能让人看懂「中间到底发生了什么」的东西。

## 1. 为什么要有中间层

直接把 PSD 变成 Prefab 也能跑通，但会有两个后果：一是 Unity 挂了就没法查，
二是「PSD 怎么解释」这件事没法脱离编辑器验证。

所以这里多切一刀：**PSD → 契约（纯数据）→ Prefab**。
契约是一份 JSON，`Runtime/Core` 全程不引 `UnityEngine`，于是 `dotnet test` 能覆盖到解析与语义的全部规则。

## 2. 顶层结构

```json
{
  "schemaVersion": "1.0.0",
  "generator": "psd2ugui",
  "document": { ... },
  "resources": [ ... ],
  "diagnostics": [ ... ],
  "root": { ... },
  "stats": { ... }
}
```

| 字段 | 说明 |
| --- | --- |
| `schemaVersion` | 契约版本。读到不认识的版本时按「尽力而为」解析，不直接报错 |
| `generator` | 产出方标识，固定 `psd2ugui` |
| `document` | 画布与来源信息（见下） |
| `resources` | 这个界面用到的图片资源清单 |
| `diagnostics` | 解析 / 语义 / 计划 / 预检产生的全部诊断 |
| `root` | 节点树根 |
| `stats` | 便于人看的统计（节点数、类型分布、标签命中数…），值都是字符串 |

C# 侧：`ContractJson.ToJsonText(document)` / `ContractJson.Parse(text)`。
**字段顺序固定**，所以同一份 PSD 反复导出，契约文本逐字节一致——方便拿去做 diff 和提交进仓库。

## 3. `document`

```json
{
  "name": "Psd2UguiForm",
  "fileName": "Psd2UguiForm.psd",
  "sourcePath": "",
  "module": "common",
  "width": 750,
  "height": 1334,
  "channelCount": 3,
  "bitDepth": 8,
  "colorMode": 3,
  "layerCount": 65,
  "resolutionPpi": 300,
  "generatorVersion": ""
}
```

`colorMode` 沿用 PSD 的编号（3 = RGB）。`bitDepth` 目前实际支持 8 与 16。

## 4. `resources[]`

```json
{
  "id": "rd3687406474517ad",
  "kind": "sprite",
  "name": "CircleFilled",
  "module": "common",
  "fileName": "CircleFilled_131x131_240037fc.png",
  "path": "sprite/common/CircleFilled_131x131_240037fc.png",
  "contentHash": "ec2d59c8b01eff5a",
  "width": 131,
  "height": 131,
  "border": { "left": 0, "bottom": 0, "right": 0, "top": 0 },
  "sourceLayerId": 646,
  "sourceLayerPath": "CircleProgress/CircleFilled",
  "sourceRect": { "x": 0, "y": 0, "width": 131, "height": 131 },
  "shared": false
}
```

| 字段 | 说明 |
| --- | --- |
| `id` | 稳定资源 ID，由来源图层 + 内容哈希派生。改图层名不会变 |
| `kind` | 目前只有 `sprite` |
| `fileName` | `名字_宽x高_内容哈希.png`；内容不变则文件名不变，Unity 不会重新导入 |
| `path` | 相对 `AssetRoot` 的路径，等于磁盘上的位置 |
| `border` | 九宫边框（像素），四边全 0 表示不切 |
| `sourceRect` | 贴图覆盖图层位图的哪一块（左上角 + 尺寸）。导出裁掉透明边时比整张图层小；补回透明边时就是整张 |
| `shared` | 是否作为共享资源导出，供其它界面 `ref` 引用 |

## 5. `root` / 节点

```json
{
  "id": "nfea96be8a972b2ba",
  "name": "100%",
  "layerPath": "CircleProgress/100%",
  "layerId": 669,
  "type": "tmp-text",
  "role": "none",
  "rect": { "x": 639, "y": 659, "width": 79, "height": 27 },
  "visible": true,
  "opacity": 1,
  "text": {
    "content": "100%",
    "fontSize": 33.33333,
    "color": { "r": 1, "g": 1, "b": 1, "a": 1 },
    "fontKey": "adobeheitistd-regular",
    "fontName": "AdobeHeitiStd-Regular",
    "align": "center",
    "wordWrap": true,
    "lineSpacing": 90,
    "tracking": -20,
    "pointText": false
  },
  "tags": { "type-source": "inferred" },
  "children": []
}
```

按需出现的字段：`clipping`、`sectionKind`、`resourceId`、`reference` + `referenceKind`、
`contentRect`、`border`、`fill`、`text`、`effects`、`tags`、`children`。

| 字段 | 说明 |
| --- | --- |
| `id` | 稳定节点 ID。增量更新靠它认领旧的 GameObject |
| `name` | 节点名（去掉标签后的部分，可被覆盖表改写） |
| `layerPath` | PSD 里的完整路径，`/` 分隔 |
| `layerId` | PSD 图层 ID（`lyid`），老的、手工拼的 PSD 可能没有，此时为 `-1` |
| `type` | 控件类型，见下表 |
| `role` | 在复合控件里扮演的角色，`none` 表示没有 |
| `rect` | 画布坐标系（左上角原点，y 向下），单位像素 |
| `contentRect` | 仅「导出时裁掉了透明边」的图层才有：贴图里真正有像素的那一块，坐标相对 `rect`。装配侧按它收窄节点矩形（位置正好补上被裁掉的左边与上边），图片就不会为了铺满整张 `rect` 被拉变形。有子节点、被状态图/`fill`/`handle`/`viewport` 角色或 `ref` 复用到的图层不裁，也就没有这个字段 |
| `opacity` | 0–1 |
| `text` | 仅文本节点。`fontSize` 是像素字号；`align` 取 `left`/`center`/`right`/`justify` |
| `effects` | 描边 / 阴影 / 发光 / 渐变等 |
| `tags` | 杂项标记。至少包含 `type-source`，值 `tag` / `override` / `inferred` |

### `type` 取值

```text
none  group  image  raw-image  text  tmp-text  fill-color  mask  panel
button  tmp-button  toggle  tmp-toggle  toggle-group  slider  scroll-view
dropdown  tmp-dropdown  input-field  tmp-input-field  list  grid  ignore
```

### `role` 取值

```text
none  background  highlight  pressed  selected  disabled  button-text  mark
label  fill  handle  viewport  horizontal-scrollbar  horizontal-scrollbar-background
vertical-scrollbar  vertical-scrollbar-background  placeholder  input-text
arrow  template  preview
```

### `effects[]`

```json
{ "kind": "drop-shadow", "enabled": true, "size": 4, "distance": 2, "angle": 120,
  "choke": 0, "color": { "r": 0, "g": 0, "b": 0, "a": 0.6 }, "opacity": 1 }
```

`kind` 取 `drop-shadow` / `inner-shadow` / `outer-glow` / `inner-glow` / `bevel` /
`solid-fill` / `gradient-fill` / `stroke` / `satin` / `pattern-fill`，对应 PSD 的十种图层效果。
渐变会额外带 `secondColor` 与 `style`；`angle` 是角度制，`size` 是像素。

`sectionKind` 只在图层组上出现，取 `open` / `closed`（对应 PSD 里的分组折叠状态）。

## 6. `diagnostics[]`

```json
{
  "severity": "warning",
  "code": "preflight.duplicate-sprite-name",
  "message": "有 2 个不同的图层都叫「ToggleBg」，`ref ToggleBg` 会取图层顺序里的第一张；建议改名或改用覆盖表指定",
  "layerPath": null,
  "nodeId": null,
  "layerId": 0
}
```

`severity` 三档：`error`（东西没出来或不对）/ `warning`（出来了但有坑）/ `info`（提醒，不影响产物）。

`code` 按前缀分组，看一眼就知道问题出在流水线的哪一段：

| 前缀 | 阶段 | 例子 |
| --- | --- | --- |
| `psd.` | 二进制解析 | `psd.warning` |
| `tag.` | 图层名标签 | `tag.unknown` |
| `override.` | 覆盖表 | `override.unmatched`、`override.parent-cycle` |
| `resource.` | 资源计划与落盘 | `resource.empty`、`resource.reference-external`、`resource.write-failed` |
| `preflight.` | 预检 | `preflight.font-missing`、`preflight.oversized-sprite`、`preflight.empty-canvas` |
| `prefab.` | Prefab 装配 | `prefab.scrollview-viewport-created`、`prefab.slider-handle-missing` |
| `text.` | 文本效果 | `text.tmp-unavailable`、`text.gradient-unsupported` |
| `effect.` | 图片节点上无法表达的图层效果 | `effect.not-applied` |
| `node.` | 节点树 | `node.ignored` |

完整清单直接搜代码里的 `Report(` 调用即可——诊断只在产生结论的地方写一次。

## 7. 另外两份文件

### 7.1 `.psd2ugui.json`（导出清单）

放在 `Assets/<AssetRoot>/manifest/<模块>/<源文件名>.psd2ugui.json`，紧挨着贴图。
记录「这个界面导出过哪些资源、内容哈希是什么、是不是共享的」。
它的作用是让**重新导出**能判断「哪些文件已经是最新的、哪些已经没人用了」。

```json
{
  "version": "1.0.0",
  "module": "common",
  "sourceFile": "Psd2UguiForm.psd",
  "resources": {
    "rd3687406474517ad": {
      "name": "CircleFilled",
      "file": "CircleFilled_131x131_240037fc.png",
      "contentHash": "ec2d59c8b01eff5a",
      "width": 131,
      "height": 131,
      "border": "0,0,0,0",
      "layerId": 646,
      "layerPath": "CircleProgress/CircleFilled",
      "shared": false
    }
  }
}
```

注意这份文件里的 `border` 是 `"左,下,右,上"` **字符串**（方便当字典键比对），
和契约里 `resources[].border` 的对象写法不一样。资源 ID 直接当对象键，所以条目里不再重复写 `id`。

### 7.2 `overrides/<模块>/<源文件名>.overrides.json`（人工覆盖表）

推断不准时用它把结果钉死，重新导出始终生效。字段与匹配规则见
[图层命名与覆盖表](TAGS.md#5-覆盖表)。

## 8. 体检报告（`.report.json`）

跟契约不是一回事：契约描述**结构**，报告描述**这一次跑的过程**。

```json
{
  "version": "1.0.0",
  "generator": "psd2ugui",
  "module": "common",
  "source": { "file": "Psd2UguiForm.psd", "path": "...", "bytes": 1086798,
              "width": 750, "height": 1334, "bitDepth": 8, "layers": 65 },
  "timing": { "parseMs": 20.2, "planMs": 12.5, "totalMs": 32.7 },
  "counts": { "nodes": 52, "groups": 3, "sprites": 12, "slicedSprites": 6,
              "sharedSprites": 3, "reusedSprites": 0, "spritePixels": 71980,
              "referencesLocal": 4, "referencesShared": 0, "referencesExternal": 0 },
  "diagnostics": { "error": 0, "warning": 2, "info": 14, "items": [ ... ] },
  "stats": { ... }
}
```

`counts.reusedSprites` 是这次从别的界面复用过来的张数，
和 `sharedSprites`（本界面作为共享导出的张数）不是一回事。

## 9. 版本策略

`schemaVersion` 目前是 `1.0.0`，与包版本无关。

- **加字段**：不动版本号。读取端一律用「取不到就用默认值」（`AsString(默认)`），
  所以老版本读新契约、新版本读老契约都不会炸。
- **改语义或删字段**：升 `schemaVersion`，并在 `docs/TASKS.md` 里记一笔迁移方式。

契约文件可以放心提交进仓库：它是纯文本、顺序固定、跨平台一致（换行统一 `\n`）。
