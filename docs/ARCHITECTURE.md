# 架构设计

> 目标读者：要改这个插件的人。看完应该知道「一个 PSD 从进来到变成 Prefab，中间经过了谁」。

## 1. 一句话概括

**一次性解析，中间落一份可读契约，最后装配 Prefab。**
解析与装配彻底分开：Core 层只认字节和数据结构，Editor 层才碰 `AssetDatabase` 与 `UnityEngine.UI`。
好处是「PSD 怎么解释」这件事可以完全脱离 Unity 用 `dotnet test` 反复验证。

## 2. 分层

```text
┌──────────────────────────────────────────────────────────────┐
│ Editor 层（asmdef: Psd2Ugui.Editor）                          │
│   窗口 / 菜单 / 批处理  →  Psd2UguiPipeline.Run(选项)          │
│   导出贴图写盘、设导入参数、存 Prefab、写报告                    │
│   唯一依赖 UnityEngine / UnityEditor 的地方                     │
└───────────────▲──────────────────────────────────────────────┘
                │ 用 Core 的结果干活，不反过来
┌───────────────┴──────────────────────────────────────────────┐
│ Core 层（asmdef: Psd2Ugui.Core，零 UnityEngine 依赖）           │
│   Psd        → 二进制 → PsdFile                                │
│   Semantics  → PsdFile → UiDocument（契约：节点树 + 资源 + 诊断） │
│   Pipeline   → UiDocument → ExportPlan（哪些图、什么名字、几宫）  │
│   Build      → UiDocument + ExportPlan → PlanNode（预制体装配计划）│
│   Imaging    → 位图合成 / 裁剪 / PNG 编码 / 九宫检测             │
│   Contract   → 契约的读写与 JSON 序列化                          │
│   Json       → 自研 JSON（不引第三方）                           │
└──────────────────────────────────────────────────────────────┘
```

`Runtime/Psd2UguiNode.cs` 是唯一一个「Runtime 里带 UnityEngine 的东西」：挂在生成的节点上记录来源图层，
增量更新靠它认领旧对象。它在 `Psd2Ugui.Runtime` 程序集里，跟 Core 分开。

## 3. 数据流

```text
file.psd
  │  PsdParser.Read            ← 只认字节，不碰像素解码
  ▼
PsdFile                         ← 图层记录 / 描述符 / 引擎数据 / 图像资源
  │  LayerRasterizer          ← 按需解码通道并合成位图（懒，问哪个才做哪个）
  │  NodeBuilder              ← 覆盖表 > 图层名标签 > 启发式推断
  ▼
UiDocument                      ← 契约：节点树 + 资源 + 诊断 + 统计
  │  ExportPlanner            ← 决定导哪些图、文件名（内容哈希）、九宫边框、跨界面复用
  ▼
ExportPlan                      ← 待写盘的贴图清单 + 资源表（回填进 document.Resources）
  ├─ SpriteExporter（Editor）  → PNG 落盘 + 导入设置 + manifest
  ├─ Preflight                → 体检诊断（缺字体、重名资源、超大图…）
  └─ PrefabPlanner            ← 节点树 → 装配计划（控件映射、锚点、九宫、模板子结构）
       │  PrefabMerge         ← 与现有 Prefab 对账：Create/Update/Unchanged/Remove/Keep
       ▼
     PrefabBuilder / IncrementalBuilder（Editor） → .prefab
```

## 4. 目录结构

```text
Psd2UGUI/
├─ package.json                     UPM 包描述
├─ Runtime/
│  ├─ Psd2UguiNode.cs               生成节点的来源标记（增量认领的凭据）
│  └─ Core/                         纯 C#，零 UnityEngine 依赖
│     ├─ Contract/                  契约模型：UiDocument / UiNode / UiResource / 诊断
│     ├─ Json/                      自研 JSON 读写
│     ├─ Psd/                       PSD 二进制解析（PSD/PSB、RLE/ZIP、描述符、引擎数据、文本）
│     ├─ Imaging/                   Bitmap / 图层合成 / 九宫检测 / PNG 编码
│     ├─ Semantics/                 标签解析、类型推断、覆盖表、图层树 → 节点树
│     ├─ Pipeline/                  导出选项与计划、预检、体检报告、共享资源表
│     └─ Build/                     装配计划、合并对账（可 dotnet 测试）
├─ Editor/
│  ├─ Psd2UguiPipeline.cs           唯一的流程编排（窗口/菜单/批处理共用）
│  ├─ Psd2UguiWindow.cs             导入窗口
│  ├─ Psd2UguiMenu.cs               菜单与右键入口
│  ├─ Import/                       路径约定、贴图落盘、导入设置、manifest、共享资源索引、Editor 预检
│  ├─ Build/                        Prefab 装配、快照、增量更新、控件工厂、TMP 后端
│  ├─ Tmp/                          TMP 相关代码单独一个程序集（没装 TMP 时整个不编译）
│  └─ Batch/                        -executeMethod 批处理入口
├─ Tests/                           Unity EditMode 测试（夹具与 dotnet 测试共用）
├─ Tools~/                          Unity 不编译的开发工具：单测、编译检查、命令行工具、宿主工程脚本
└─ docs/
```

## 5. 几个关键设计

### 5.1 稳定 ID：为什么要有

`StableId.NodeId / ResourceId` 由「图层路径 + 图层 ID + 内容」派生出固定长度的哈希。
同一个 PSD 反复导出，ID 不变，于是：

- `Psd2UguiNode.NodeId` 能认出「这个 GameObject 是上次导出时那一层」；
- 资源文件名里带内容哈希，图片没变就不重写文件，Unity 也不会重新导入。

节点名和图层路径都不参与「身份」，只影响显示名——所以**在 PSD 里改图层名不会让增量更新认输重建**。

### 5.2 增量更新：只动自己管的东西

`PrefabMerge.Diff(现有快照, 新计划)` 把每个节点判成六种动作之一：

| 动作 | 含义 |
| --- | --- |
| `Create` | 新计划里有、现有里没有 |
| `Update` | 有对应物，但受管属性指纹（`PlanSpec.Hash`）变了 |
| `Unchanged` | 有对应物且属性一致，一律不碰 |
| `Reparent` | 位置变了（换父节点或换兄弟顺序） |
| `Remove` | 上次标记为「工具生成」、这次计划里没有了 |
| `Keep` | 认领不上、也不是工具生成的 → 当作人工内容，保留 |

`PrefabSnapshot.Collect` 负责「认领」：先按 `Psd2UguiNode.NodeId` 认，认不到再按「同父同名」认模板节点。
**认领不上的对象一律当人工内容**——宁可留一个没用的节点，也不能删用户手写的界面。

`IncrementalBuilder` 按三趟执行，顺序很重要：

1. `Restructure`：只动父子关系（增删节点、换父）
2. `Refresh`：刷受管属性（RectTransform / 文本 / 颜色 / 九宫）
3. `Prune`：清理该删的组件与节点

一趟干一件事，出错时也好定位是「结构错了」还是「属性错了」。

### 5.3 控件语义：谁说了算

优先级从高到低：

```text
覆盖表（人工最终裁决）
  > 图层名标签（美术在 PSD 里写）
    > 文本 / 分组 / 纯色等结构性事实
      > 名字关键词启发式（button、toggle、bg…）
```

每个节点都会在 `Tags["type-source"]` 里记下结论的来源（`tag` / `override` / `inferred`），
窗口里直接显示，避免「为什么它变成 Button 了」这种扯皮。
推断器是接口（`ITypeInferrer`），要接自己的规则（比如项目里的命名规范、甚至外挂模型）只需要实现它。

### 5.4 跨界面复用

`SharedSpriteTable` 把每个模块导出的 `.psd2ugui.json` 读成一张表。
图层名写 `ref ButtonBule` 时，先在本文件里找，找不到就去表里按「模块 / 名字」找，
命中就把资源指向已有 PNG，不再导出一份像素。

### 5.5 体检报告与预检

两者分工不同：

- **预检（Preflight）**：写文件**之前**跑，目标是「别写出一堆没用的垃圾」——
  空画布、重名资源、超大贴图、缺字体、退化的九宫。
- **体检报告（ImportReport）**：跑完**之后**汇总，记下这次的输入、耗时、产物数量、全部诊断。

两者共用同一套诊断渠道（`document.Diagnostics`），所以窗口、报告、控制台看到的是同一批事实。

## 6. 扩展点

| 想做的事 | 改哪里 |
| --- | --- |
| 加一种控件类型 | `UiElementType` 加枚举 → `LayerTag` 加标签 → `PrefabPlanner` 出一个 `ControlKind` → `ControlFactory` 装配 |
| 加一条类型推断规则 | 实现 `ITypeInferrer`，挂到 `NodeBuildOptions.Inferrers`（排在启发式规则之前） |
| 加一种诊断 | 在产生结论的地方 `document.Report(severity, code, message, node)`，其余不用管 |
| 换文本后端 | 实现 `ITmpBackend`，替换 `TmpBackend.Current` |
| 从别的工具产出契约 | 写一个符合 [契约格式](CONTRACT.md) 的 JSON，Editor 侧按需扩展读取入口 |
| 自己做美术侧导出面板 | 按契约 JSON 格式导出即可，实现与引擎侧解耦 |

## 7. 为什么不引第三方库

| 备选 | 为什么没用 |
| --- | --- |
| Aspose.PSD | 商业授权 + 水印 |
| PSD.NET（Photoshop 官方） | 需要装 Photoshop |
| Newtonsoft.Json | 契约 JSON 结构很简单，自研 200 行足够，还少一个版本冲突源 |

代价是解析器要自己维护，所以配套了 `Tools~/psd-tools-verify/`（拿 Python 的 psd-tools 交叉校验图层明细）
和大量用自造字节流跑的单元测试。
