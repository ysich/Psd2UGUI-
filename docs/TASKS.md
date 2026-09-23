# PSD2UGUI 任务清单

> 本文件是开发的总纲与进度表。每完成一步，勾选并提交一次 git commit。
> 最后更新：2026-09-24

---

## 0. 项目定位

**目标**：Unity 端导入 PSD → 解析图层树 → 导出可继续编辑的 uGUI Prefab
（含 Sprite 资源、九宫、文本与复合控件），并支持重新导出的增量更新。

**范围边界（明确不做）**

| 不做 | 原因 |
| --- | --- |
| 运行时加载 PSD | 只做 Editor 侧工具链，产物是 Prefab 与 Sprite |
| 第三方 PSD / JSON 库（Aspose、Newtonsoft、PSD.NET 等） | 避免水印、授权与版本耦合问题，解析器自研 |
| 云端 AI 识别服务 | 只留 `ITypeInferrer` 接口与本地提示词钩子，不外联 |
| 美术侧 Photoshop 面板 | 与引擎侧解耦，但导出与文档同构的契约 JSON 便于后续扩展 |
| 业务框架代码（项目路由、事件、列表框架） | 保持工具通用性，不绑定任何项目 |

**验收基线**：`dotnet test` 全绿 + Unity EditMode 测试全绿 + 用真实 PSD 跑通
「解析 → 导出资源 → 生成 Prefab → 重新导出保持增量」全流程，且无 Error 级诊断。

---

## 1. 设计取舍：三个参考仓库优点的落地

| 来源 | 值得借鉴的优点 | 本项目落地方式 |
| --- | --- | --- |
| `sunsvip/PSD2UGUI_X` | Unity 内一步解析成可编辑节点树再生成 Prefab；命名标签指定控件类型；自动九宫；资源复用（ref/refp）；重新解析保留已有结构 | 同一条主流水线：解析 → 节点树（可人工改类型）→ 生成 Prefab；标签体系自研；自动九宫；稳定 ID + 身份映射实现复用与增量 |
| `YoyoRD/PSD2UI` | 引擎无关中间契约（独立 Schema、稳定资源 ID、诊断列表）；规则层与宿主层分离；测试充分；文档详尽 | `Runtime/Core` 输出带版本号的契约 JSON + 诊断数组；Core 无 UnityEngine 依赖可脱离 Unity 测试；文档覆盖使用/架构/限制 |
| `ZeroUltra/PSD2Unity` | 体量小、无依赖、MIT、易读易改；回调式扩展点 | 全仓库零第三方依赖；提供 `Psd2UguiPipeline` 编程接口 + 构建钩子回调；MIT |

**刻意规避它们的短板**

- 不复刻 sunsvip 的闭源 DLL 与授权限制 → 全部源码交付。
- 不复刻 YoyoRD 只做一半（无 Unity 端）→ 端到端直达 Prefab。
- 不复刻 ZeroUltra 的能力上限（只有 Image + 绝对坐标、文本被栅格化）→ 控件语义、文本、九宫、复用、增量齐备。

---

## 2. 技术架构

```text
Psd2UGUI/                          # UPM 包
├─ package.json
├─ Runtime/Psd2UguiNode.cs         # 生成节点的标记组件（记录来源图层，增量更新靠它认领）
├─ Runtime/Core/                   # 纯 C#，零 UnityEngine 依赖（可 dotnet 测试）
│  ├─ Contract/                    # 中间契约：文档、节点、资源、诊断、JSON 写出
│  ├─ Psd/                         # PSD 二进制解析器（PSD/PSB、RLE/ZIP）
│  ├─ Imaging/                     # 位图、裁剪/去空、PNG 编码、九宫检测
│  ├─ Semantics/                   # 标签解析、类型推断、角色分配、覆盖表、节点树构建
│  ├─ Pipeline/                    # 编排、选项、预检、体检报告、共享资源表
│  └─ Build/                       # 预制体装配计划（控件映射、布局换算，可 dotnet 测试）
├─ Editor/                         # Unity Editor 侧
│  ├─ Psd2UguiPipeline.cs          # 流程编排（窗口 / 菜单 / 批处理共用）
│  ├─ Psd2UguiWindow.cs            # 导入窗口
│  ├─ Psd2UguiMenu.cs              # 菜单与右键入口
│  ├─ Batch/                       # -executeMethod 批处理入口
│  ├─ Import/                      # 读取 PSD、Sprite 落盘、导入设置、资源复用、预检
│  ├─ Tmp/                          # 可选：TMP 文本后端（defineConstraints 控制是否参与编译）
│  └─ Build/                       # Prefab 装配、控件工厂、角色连线、TMP 效果、增量更新
├─ Tests/                          # Unity EditMode 测试（NUnit + asmdef）
├─ Tools~/                         # Unity 忽略目录
│  ├─ CoreTests/                   # dotnet 测试工程（直接编译 Runtime/Core 源码）
│  ├─ CoreNetStandard/             # 把 Core 按 netstandard2.1 编一遍（Unity 兼容性检查）
│  ├─ EditorCompile/               # 引用 Unity DLL 编译 Editor 脚本（不开编辑器的语法/API 检查）
│  ├─ PsdDump/                     # 命令行解析与导出工具
│  ├─ psd-tools-verify/            # 与 psd-tools / Pillow 交叉校验的脚本
│  ├─ run-tests.sh                 # 一条命令跑完所有离线检查
│  └─ dev-project.sh               # 生成宿主工程并无头运行 EditMode 测试
└─ docs/                           # 任务清单、架构、契约、使用、限制
```

**导出产物布局（`AssetRoot` 默认 `Assets/PSD2UGUI`）**

```text
Assets/PSD2UGUI/
├─ sprite/<模块>/<名字>_<宽>x<高>_<哈希>.png    # 贴图，同模块内按内容共用
├─ manifest/<模块>/<源文件>.psd2ugui.json       # 资源身份映射（增量更新用）
└─ contract/<模块>/<源文件>.json                # 契约 JSON（排查用）
```

**程序集划分**

| 程序集 | 依赖 | 说明 |
| --- | --- | --- |
| `Psd2Ugui.Core` | 无 | 解析、契约、位图、PNG、语义推断、装配计划，纯 C# |
| `Psd2Ugui.Runtime` | UnityEngine | 生成节点的标记组件 `Psd2UguiNode`，必须能进玩家包 |
| `Psd2Ugui.Editor` | Core、Runtime、UnityEditor、UGUI | 资源导出与 Prefab 装配 |
| `Psd2Ugui.Editor.Tmp` | Editor、Unity.TextMeshPro | 可选：装了 TMP 才编译（`defineConstraints: PSD2UGUI_TMP`） |
| `Psd2Ugui.Tests.EditMode` | 上述两者、NUnit | EditMode 测试 |

**关键决策**

1. **自研 PSD 解析器**：分层解析（文件头 → 颜色模式 → 图像资源 → 图层与蒙版 → 图像数据），
   不支持的特性一律转成诊断条目，绝不静默丢数据。
2. **契约先行**：先产出 `contract.json`（版本化），再由 Editor 层消费生成 Prefab，
   便于排查、便于将来接入别的引擎。
3. **稳定身份**：每个节点与资源都有确定性 ID（层级路径 + 图层 ID + 内容哈希），
   增量更新与资源复用都基于它。
4. **两段式识别**：命名标签（显式）+ 启发式推断（隐式）+ 人工覆盖表（兜底），
   与 sunsvip 的 AI 识别保持同构的接口位，但不引入云端依赖。
5. **归属可辨识**：工具生成的节点带 `Psd2UguiNode` 标记组件，增量更新只增删改自己拥有的部分。

---

## 3. 步骤清单

### Step 0 · 仓库初始化与任务清单

- [x] 建立仓库骨架：`package.json`、`LICENSE`、`.gitignore`、`.gitattributes`、`README.md`
- [x] 输出本任务清单 `docs/TASKS.md`
- **验收**：仓库可 `git status` 干净提交；清单覆盖全部后续步骤
- **提交**：`chore: 初始化 PSD2UGUI 仓库与任务清单`

### Step 1 · Core 契约与数据模型

- [x] `Contract/`：`UiDocument`、`UiNode`、`UiElementType`、`UiRole`、`UiResource`、`UiDiagnostic`、`ContractJsonWriter`
- [x] 零依赖 JSON 读写器（转义、缩进、确定性字段顺序、回读解析）
- [x] `Tools~/CoreTests` dotnet 测试工程骨架（直接编译 `Runtime/Core`）
- [x] `.gitignore` 放行 `Tools~/`（全局 gitignore 的 `*~` 规则会把它整个排除掉，导致工具与测试进不了仓库）

- **验收**：`dotnet test` 通过；契约 JSON 可被解析回模型（往返测试）
- **提交**：`feat(core): 契约数据模型与 JSON 写出`

### Step 2 · PSD 容器解析

- [x] 文件头（`8BPS`/PSD/PSB）、颜色模式段、图像资源段
- [x] 图层与蒙版段：图层记录、通道信息、混合模式、不透明度、裁剪标记、flags
- [x] 附加图层信息：`luni`（Unicode 名）、`lyid`（图层 ID）、`lsct`（分组）、`SoCo`（纯色填充）、`TySh`（文本）、`lfx2`（图层效果）、矢量蒙版 `vmsk/vsms`
- [x] 图像数据解码：Raw / RLE / ZIP / ZIP+Prediction，8 / 16 / 32 位
- [x] 未知块与不支持特性 → 诊断条目
- [x] `Tools~/PsdDump` 命令行查看器（`--layers` 输出图层明细 JSON）
- [x] `Tools~/psd-tools-verify` 交叉校验工具链（与 psd-tools 逐字段对照）
- **验收**：`dotnet test` 80 项全绿；真实 PSD 两个样本共 104 个图层节点与 psd-tools 逐字段一致（矩形、不透明度、可见性、裁剪、分组类型、文本内容/字体/字号/颜色/对齐、效果种类、纯色填充颜色），解析期间零警告、零异常
- **提交**：`feat(core): 自研 PSD 二进制解析器`

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 文本层字号恒为 0、颜色恒为白 | 样式字段在 `StyleSheet` → `StyleSheetData` 两层里，段落对齐同理在 `Properties` 里；颜色是 `<< /Type 1 /Values [A R G B] >>` 而不是数组 | 按真实层级取值，颜色按 ARGB 解析并加数组回退 |
| 字体名取不到 | `FontSet` 挂在 `ResourceDict` 下，与 `EngineDict` 平级而非其子节点 | 先取平级 `ResourceDict`，再回退 `EngineDict` 里的同名节点 |
| 文档级附加信息块报签名异常 | Photoshop 会在每块之后补 0~3 字节对齐到 4 字节，补位不计入长度字段 | 按块起点做 4 字节对齐后再读下一块 |
| 图层全部被判为隐藏 | flags 的第 2 位（0x02）置位表示“隐藏”，与规范字面描述相反 | `Visible = (flags & 0x02) == 0` |
| 部分图层效果多出一堆没开的效果 | `lfx2` 里存在 `present=true` 但 `enab=false` 的残留样式，只按 `present` 过滤会误导出 | 以 `enab` 为准，两个标志都为真才生效 |
| 图层数比实际多时报错中断 | 追加数据长度异常时图层记录会读到段外 | 单条记录解析失败降级为警告并停止读取剩余图层 |
| 分组层级多出 13 个空节点 | `lsct=3` 的收尾标记只是“开括号”，不是图层 | 收尾标记不生成节点，仅用于还原层级 |

### Step 3 · 图层树与位图合成

- [x] 图层 → 树：分组嵌套、`lsct` 折叠、隐藏层策略（`IncludeHiddenLayers` 可整体打开）
- [x] 通道合成：RGB(A)、透明层、1 位位图、灰度、CMYK 基础支持、用户蒙版与裁剪链
- [x] 文本层：描述符解析（内容、字体、字号、颜色、对齐、变换矩阵）
- [x] 纯色填充层按 `SoCo` 颜色铺满；渐变/图案填充层没有像素通道，产出诊断而不是静默丢弃
- [x] `Bitmap`：裁剪、包围盒、`TrimTransparent`（去空）、覆盖率、直通 alpha 合成
- [x] 图层不透明度不预乘进位图（留给 Prefab 的 `Image.color.a`），导出的贴图保持“图层自身像素”
- **验收**：合成位图与预期像素一致（构造用例断言）；树结构与 PS 面板层级一致
- **验收结果**：`dotnet test` 120 项全绿；两个真实 PSD 各 39 个图层与 psd-tools **逐像素完全一致**（最大差值 0），
  图层顺序/矩形/不透明度/可见性逐项一致；用同一份图层数据在 numpy 里独立重算的合成图与自研合成结果
  4002000 字节全等（顺序、不透明度、混合公式同时被验证）
- **提交**：`feat(core): 图层树构建与位图合成`

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 栅格化代码编译不过 | 位深在 `PsdFile` 上（`BitDepth`），图层上只有 `BytesPerSample`；颜色模式要用 `file.Mode` 而不是和 `int` 比较 | 位深判断统一走 `file.BitDepth`，颜色模式统一走 `file.Mode` |
| 16 位样本在中间值上偏 1 | `(value * 255 + 32767) / 65535` 的偏移少半个单位，`0x8000` 会落到 127 | 改成浮点 `value * 255 / 65535` 再四舍五入 |
| 无法用 psd-tools 的整图合成做对照 | psd-tools 的 `composite()` 会烘焙图层效果（本例有投影/描边/发光/斜面和渐变叠加），还会用到 scipy/aggdraw/scikit-image | 改用「图层像素 + 独立 numpy 合成」对照，psd-tools 整图合成只作参考信息 |
| 搞不清图层记录是自下而上还是自上而下 | 规范里没写死在代码里，容易凭感觉假设 | 用样本实证：第一条记录是 `BgColor.fillcolor`（0,0 起的全画布背景），且与 psd-tools `descendants()` 顺序一致，故合成时按记录顺序自下而上叠加 |


### Step 4 · 语义标签与类型推断

- [x] 标签解析：`Name.btn` / `Name.text` / `Name.tmp` / `Name.img` / `Name.rimg` / `Name.sv` / `Name.sld` / `Name.tg` / `Name.ipt` / `Name.dpd` / `Name.panel` / `Name.mask` / `Name.color`，共 60 余个类型标签
- [x] 角色标签：`bg` / `onover` / `press` / `select` / `disable` / `bttxt` / `fill` / `handle` / `vpt` / `hbar` / `vbar` / `dpdicon` / `placeholder` / `ipttxt` / `mark` / `tglb` / `ignore` 等
- [x] 开关标签：`sliced`（九宫）、`ignore`（跳过整棵子树）、`ref` / `refp`（复用图片 / 子 Prefab）
- [x] 启发式推断：无标签时按文本层 → 分组 → 纯色填充 → 名称关键词 → 像素数据依次判断；写了角色标签的节点不抢控件类型
- [x] 节点树构建：分组嵌套、绝对坐标、稳定 ID、隐藏层策略、文本与纯色填充映射
- [x] 覆盖表：JSON 形式的 `overrides`，按 `名字` / `#图层ID` / `LayerPath` 匹配，支持类型、角色、改名、忽略、九宫、改层级、指定资源
- [x] 未匹配的覆盖表条目、改层级造成循环都会给出诊断
- [x] `ITypeInferrer` 接口（自定义推断器优先于启发式，返回 None 时交给下一个）
- [x] 标签表与覆盖表格式写进 `docs/TAGS.md`
- **验收**：标签用例表与推断用例表全部通过；误判可被覆盖表修正
- **验收结果**：`dotnet test` 194 项全绿（标签表 45 条、推断规则 7 条、节点树与覆盖表 21 条）；
  真实样本解析出 52 个节点（3 个分组），类型分布
  `button=2, dropdown=1, fill-color=6, group=2, image=14, input-field=1, panel=1, scroll-view=2, slider=1, tmp-button=3, tmp-text=17, toggle=2`，
  标签命中 42 个、推断补齐 31 个、引用 4 个，只有 1 条 Info 诊断（`CircleFilled.filled.bg` 里的 `filled` 是不认识的标签）；
  `--overrides` 实测可以改类型 / 角色 / 改名 / 改层级，未匹配条目给出警告
- **提交**：`feat(core): 图层语义标签与控件类型推断`

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| `ToggleLabel.label` 只识别出类型、角色丢了 | `label` 同时出现在类型表与角色表里，而类型表先被查 | 类型表里去掉 `label`，它只是一个角色 |
| `Sky.ignore` 没有跳过图层 | `ignore` 先在类型表里命中，函数提前返回，忽略开关没被赋值 | 忽略开关放在类型查表之前处理，同时把类型设为 `Ignore` |
| 一条覆盖表只能改一个字段 | 取值用的是“第一条命中”，类型与角色写成两条时后一条永远不生效 | 改成取“最后一条命中”，多条命中时后面的覆盖前面的 |
| 覆盖表写错名字却悄悄不生效 | 匹配不到节点时没有任何反馈 | 匹配不到就给 `override.unmatched` 警告 |
| `ref X` 这类图层被判成按钮 | 引用节点也走名字关键词 | 引用节点先判成 Image（分组除外），类型交给标签或覆盖表 |

### Step 5 · 九宫检测与 PNG 编码

- [x] 九宫检测：按“相邻行/列完全一致”定位可拉伸区，输出 `left/bottom/right/top`
- [x] 逐块验证拉伸等价于复制：中间块纯色、上下条带横向一致、左右条带纵向一致，任一不满足就放弃九宫
- [x] 最小可拉伸图裁剪：把可拉伸区压到 1 像素，边框保持原值
- [x] 自研 PNG 编码器：RGBA32、zlib(Deflate) + CRC32 + Adler32、五种行滤波按行择优
- [x] 与 Unity 侧无耦合，`Runtime/Core` 可独立测试
- **验收**：编码结果可被系统图片工具正确解码；九宫用例（固定边框、渐变边框、纯色、无边框）判定正确
- **验收结果**：`dotnet test` 212 项全绿（PNG 6 项、九宫 12 项）；
  PNG 由测试内的独立解码器（校验签名 / 每个 chunk 的 CRC / zlib 头 / Adler32 / 反滤波）解回后像素全等，
  CRC32 与 Adler32 也对上了标准测试向量；真实样本导出的 33 个 PNG 用 Pillow 解码后，
  按九宫还原成原尺寸与 psd-tools 的图层像素**逐像素一致**（误差 0），
  其中 20 个判出九宫（滚动条 5~20 像素边框、输入框 9/5/9/13），文字图与渐变图正确判定为不可九宫
- **提交**：`feat(core): 自动九宫检测与 PNG 编码器`

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 文字图被判出“九宫 258/201”这种荒唐结果 | 只检查了可拉伸区是纯色，没检查边框条带；文字字缝恰好是一列透明，就被当成了可拉伸区 | 增加逐块验证：上下条带要横向一致、左右条带要纵向一致；边框为 0 时检查退化成对整幅图的要求，正好挡住这类误判 |
| `Mark`/`Arrow` 这类小图标偶发判出九宫 | 图标里巧合出现的连续相同行/列 | 同上，逐块验证会直接否掉 |
| 导出 PNG 用 `Bitmap.Pixels` 直接写会偏色 | 位图是直通 alpha，PNG 也必须直通 | 编码器不做任何预乘，与位图语义保持一致 |

### Step 6 · 资源导出与导入设置（Editor）

- [x] `ExportPlanner`：栅格化 → 去空 → 九宫检测 → 内容去重 → 绑定资源 ID → 解析 `ref` / `refp`（纯 Core，可脱离 Unity 测试）
- [x] `SpriteExporter`：落盘 PNG → `TextureImporter` 配置（Sprite、Pivot、九宫 Border、关 mipmap/读写、不压缩、MaxSize 跟着图长）
- [x] 资源复用：内容哈希 + 九宫 → 同一张图只落一份；内容没变就不重写文件、不改导入设置
- [x] 身份映射 `.psd2ugui.json`（资源 ID → 文件 / 内容哈希 / 九宫 / 来源图层）与契约 JSON 落盘
- [x] 路径规划：`Assets/PSD2UGUI/{sprite,manifest,contract}/<模块>/…`，重名冲突与越界目录诊断
- [x] `Tools~/EditorCompile`：引用 Unity 自带 DLL、用 netstandard2.1 编译 Editor 脚本（不开编辑器也能查语法/API 错误）
- [x] `Tools~/psd-tools-verify/check_export.py`：独立第三方校验导出目录（Pillow 解码 + 九宫还原逐像素比对）
- **验收**：导出目录结构符合规划；重复导出不产生重复资源；导入设置断言通过
- **验收结果**：`dotnet test` 256 项全绿（导出计划 22 项、身份映射 11 项、图层 ID 兜底 3 项）；
  Editor 脚本 0 错误 0 警告通过 netstandard2.1 + Unity 2022.3 DLL 编译；
  真实样本 `Psd2UguiForm.psd` 端到端导出 12 张 PNG（6 张九宫、3 张被 `ref` 共用、0 条外部引用），
  用 Pillow 解码 + 九宫还原与 psd-tools 图层像素**逐像素一致 12/12**、0 问题；
  同一份 PSD 连导两次，契约 JSON 文本完全一致
- **提交**：`feat(editor): Sprite 导出与导入设置`

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 两张不同的图被合成了一张 | 夹具里的图层没有 `lyid`，所有图层的 ID 都是 -1，按 ID 查图层永远只查到第一个 | 解析器给缺 `lyid` 的图层补一个不小于任何真实编号的合成 ID（并在警告里说明），夹具默认也带 `lyid`，另留一个不带 `lyid` 的用例守兜底 |
| 「图一样但九宫不同」的两张资源会撞文件名 | 文件名只用了内容哈希，没算上九宫 | 文件名种子改用 `Key`（内容哈希 + 九宫），两条资源自然分开 |
| 导出脚本报「还原尺寸不符 (181,51) vs (51,181)」 | 校验脚本里 `np.ix_(xs, ys)` 把行列写反了 | 改成 `np.ix_(ys, xs)`；这类错误正好说明独立校验脚本值得有 |
| 大图被 Unity 悄悄缩小 | `TextureImporter` 默认 `maxTextureSize=2048`，超过就缩图且不报错 | MaxSize 按图片边长从 2048 逐级翻倍到够用（上限 8192），超 8192 另给警告 |
| 反复导出把工程拖慢 | 每次导出都重写 PNG 并 `SaveAndReimport` | 内容哈希没变就不写文件；导入设置逐项比对，只有真的不一致才写回重导 |

### Step 7 · Prefab 装配（Editor）

- [x] `PrefabPlanner`：契约节点树 → 装配计划（控件种类、相对矩形、槽位、被吸收的图层），纯 Core 可测试
- [x] `RectTransform` 布局：PSD 像素 → 左上锚点（anchor/pivot 0,1 + 负 Y 偏移），模板节点用拉伸锚点
- [x] 控件工厂：Image、RawImage、Text、TMP Text、FillColor、Button、Toggle、Slider、Scrollbar、ScrollView、Dropdown、InputField、Panel、Mask
- [x] 角色连线：`targetGraphic`、`graphic`（勾选图）、`fillRect`、`handleRect`、`viewport`、`content`、`captionText`、`itemText`、`textComponent`、`placeholder`、滚动条
- [x] 按钮四态（悬停/按下/选中/禁用）吸收成 `spriteState`，不生成多余对象
- [x] 文本样式：字号/颜色/对齐/字体路由（按 PSD 字体名找工程字体，找不到给诊断）
- [x] 文本效果：描边/外发光 → `Outline`，投影 → `Shadow`，渐变 → TMP 顶点渐变（uGUI Text 给诊断）
- [x] `Psd2UguiNode` 标记组件（稳定 ID、图层路径、图层 ID、角色、来源 PSD）
- [x] 根节点可选 Canvas + CanvasScaler（参考分辨率取 PSD 画布）+ GraphicRaycaster
- [x] `PsdDump --prefab-plan out.json`：命令行核对映射结果，不开 Unity 也能看
- **验收**：EditMode 测试断言层级、组件、关键属性与九宫 Border 正确
- **验收结果**：`dotnet test` 291 项全绿（装配计划 20 项：类型映射、相对坐标、四态吸收、
  视口/内容重挂、下拉模板复用、TMP 改 Text、未跑导出时的提示等）；
  Editor 脚本 0 错误 0 警告通过 netstandard2.1 + Unity 2022.3 DLL 编译（含 TMP 后端）；
  真实样本 `Psd2UguiForm.psd` 生成 57 节点计划：`Button=2 Dropdown=1 FillColor=6 Image=14
  InputField=1 Panel=1 Rect=8 ScrollView=2 Scrollbar=2 Slider=1 Text=2 TmpText=15 Toggle=2`，
  0 Error / 3 Warning（设计稿本身没有滑块图层，滚动条与滑条退化成整条可拖），
  按钮拿到 `targetGraphic=ButtonBule`，下拉框复用设计稿里的列表结构并把列表项移进 Content
- **提交**：`feat(editor): uGUI Prefab 装配与文本效果`

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| Editor 代码报 `BuildOptions` 引用不明确 | UnityEditor 里也有个 `BuildOptions`（构建参数枚举），和我们的同名 | 用 `using BuildOptions = Psd2Ugui.Core.Build.BuildOptions;` 别名固定语义（编译检查提前抓到的） |
| 按钮的底图被「抬」到根节点会改变设计稿几何 | 一开始想让按钮根节点自己带底图，但根节点尺寸与图层尺寸未必一致 | 底图留在原层级，只把 `targetGraphic` 指向它；uGUI 的点击判定按 Graphic 冒泡，照样点得到 |
| 按钮的多出四个可见图层 | 四态图层也当普通子节点生成了 | 四态只作为 `spriteState` 的贴图来源，吸收进组件、不生成对象，并记进 `ConsumedNodeIds` |
| 下拉框出现「设计稿列表 + 组件列表」两份 | uGUI Dropdown 必须有 Template，于是无条件自造了一套 | 设计稿里已有列表 + 列表项时优先复用：Template 指向设计稿的列表，列表项重挂到 Content 下并改写成局部坐标 |
| `DropDown`/`InputField` 的文字类型对不上 | uGUI 的 Dropdown / InputField 只认老 `Text` 组件 | 在计划层就把这两处的 TMP 图层改成 `Text`，而不是在 Editor 侧偷偷换组件（有诊断说明） |
| 组件挂着空 `handleRect`，拖动没反馈 | 设计稿常常没画滑块图层 | `Slider`/`Scrollbar` 的 handleRect 退化成自身矩形，并给提示 |
| `GetComponent<T>() ?? AddComponent<T>()` 有隐患 | `??` 走的是 CLR 空判断，绕过 Unity 重载的 `==`（已销毁对象是「假空」） | 一律写成 `if (x == null) x = ...` |
| 源码目录 `Runtime/Core/Build`、`Editor/Build` 没进仓库 | 标准 Unity `.gitignore` 里的 `[Bb]uild/` 会把任何叫 `Build` 的目录都排除掉，`git add -A` 也救不回来（这一步是靠对提交内容再核对一遍才发现的） | 在 `[Bb]uild/` 之后加 `!Runtime/Core/Build/**`、`!Editor/Build/**` 放行；提交后要核对 `git show --stat` 与 `git ls-files` |

### Step 8 · 增量更新与资源复用

- [x] 重新导出时按稳定 ID 匹配：更新属性、新增节点、移除消失节点
- [x] 保留人工改动：用户新增的子节点/组件不被删除，被工具管理的属性才覆盖
- [x] `ref` / `refp`：跨界面共享图片与子 Prefab 复用
- [x] 冲突与失效诊断（资源被占用、Prefab 被改动等）
- **验收**：EditMode 测试「生成 → 手动改 → 重新生成」后人工改动仍在，且新增/删除正确
- **验收结果**：见下（本机无法无头跑 Unity，EditMode 部分顺延到 Step 11，这里给出等价的手工步骤与命令行证据）
- **提交**：`feat(editor): 增量更新与资源复用`

**这一版怎么做的**

| 部件 | 位置 | 职责 |
| --- | --- | --- |
| `PlanSpec.Hash` | `Runtime/Core/Build` | 受管属性指纹（种类/矩形/锚点/显隐/透明度/贴图/九宫/颜色/子预制体/文本/效果），重新导出时靠它判断「这个节点到底变没变」 |
| `PlanKeys` / `ExistingNode` / `PrefabMerge.Diff` | `Runtime/Core/Build` | 层级键（稳定 ID，模板节点用 `#名字`）+ 差异比对，产出 `Create/Update/Unchanged/Reparent/Remove/Keep` |
| `PrefabSnapshot` | `Editor/Build` | 读现有预制体：带标记的按稳定 ID 认领（并记下「现在」挂在谁下面），模板节点按同父同名子对象认领，认领不到的算遗留，没标记的当人工内容 |
| `IncrementalBuilder` | `Editor/Build` | 三趟执行：① 只动结构（新建 / 换父级）② 刷属性（此时子件都已在索引里，连线一次找齐）③ 清理 |
| `SharedSpriteTable` | `Runtime/Core/Pipeline` | 跨界面复用的共享贴图表：本文件里没有的 `ref` 按「模块 / 名字」来这里找 |
| `SharedResourceIndex` | `Editor/Import` | 扫模块里的身份映射 → 共享表；反查「哪些 PNG 还有别的界面在用」 |
| `SpriteExporter.CleanObsolete` | `Editor/Import` | 真的删掉失效贴图，但被别的界面引用的一律跳过 |

判定规则（都为「人工改动不被冲掉」）：

- 指纹一样 → 完全不动这个节点（人手改过的坐标、颜色、字体、脚本、子对象都留着）；
- 指纹变了 → 只刷工具管的属性，人手加的子节点/组件不碰；
- 设计稿里删掉的图层 → 删掉对应节点，但它身上挂着人工内容时改为保留并给 Info；
- 没有标记的对象 → 一律当人工加的，永远不碰；同一个生成节点被复制成多份（NodeId 撞车）也按人工内容处理；
- 根节点永远认领现有对象，绝不重建（换根等于让使用者重连所有引用）。

**验收结果**

- `dotnet test Tools~/CoreTests` **321 项全绿**（Step 7 是 291，本步 +30：合并判定 19 项、共享复用 11 项）。
  - 合并判定：指纹稳定性（含极小抖动不算变化）、键路径规则、未变/已变/新增/换父级/删除/保留/无标记不碰/模板节点按键认领；
  - 共享复用：共享表读取与坏数据容错、同模块优先、跨模块复用不落盘、本文件同名图优先、
    契约资源表带上复用资源、复用记录进身份映射、`FindObsolete` 不把复用的图算失效；
  - 身份映射新增覆盖：`IsManifestPath` 后缀判定、复用条目往返不变。
- Editor 脚本经 netstandard2.1 + Unity 2022.3 DLL 编译：**0 错误 0 警告**（含新增 `PrefabSnapshot`、`IncrementalBuilder`、`SharedResourceIndex`）。
- 命令行端到端（不开 Unity，用真实样本 + 一个只含 `ref Logo.img` 的小 PSD）：
  1. 先导出真实样本 `Psd2UguiForm.psd`（12 张 PNG + `manifest/common/Psd2UguiForm.psd2ugui.json`）；
  2. 再导 `ref Logo.img` 那个 PSD 并挂上共享库：
     `PsdDump /tmp/psd8_ref.psd --module common --shared-dir /tmp/psd8_export/manifest --export-dir /tmp/psd8_ref_out --prefab-plan /tmp/psd8_ref_plan.json`
     → **新写 0 张 PNG**，契约 `resourcesShared=1 / spritesReused=1 / referencesExternal=0`，
     诊断从 `resource.reference-external`（找不到）变成 `resource.reference-shared`（复用），
     装配计划里该节点绑到了别人导出的 `Logo_483x74_1f77e51a.png`，引用方自己的身份映射里也记了一笔 `shared=true`。

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 增量刷新一次，组件就多一套 | `ApplyVisual` 原来一律 `AddComponent`，重建路径没问题，刷新路径就翻倍 | 组件统一走 `GetOrAdd`；顺便把「类型换了」的旧视觉件（Image / RawImage / Text / TMP / Mask / CanvasGroup）摘掉 |
| 共享资源库读到 0 张图 | `Psd2UguiManifest.FileName` 是**后缀**（`.psd2ugui.json`），拿它和 `Path.GetFileName` 求相等永远为假 | 加 `Psd2UguiManifest.IsManifestPath`，CLI 与编辑器共用同一处判定 |
| 复用的共享图被当成「失效资源」清掉 | 引用方没在身份映射里记这一笔，产出方重新导出时以为没人用 | 引用方的 `manifest.Update` 记录复用条目 + `FindObsolete` 保留复用 ID + 删文件前反查「还有谁在用」 |
| 根节点差点被整棵重建 | 设计稿换了根图层名时，按 ID 匹配不到旧根 | 根节点在快照阶段就强制认领现有根对象（按计划键对齐） |
| `PrefabUtility` 相关逻辑没法在本机跑 | 本机 Unity 有 GUI 实例占着授权锁，`-batchmode` 起不来 | 用 netstandard2.1 + Unity DLL 编译检查兜住 API/语法错误，行为验证留给 Step 11 的 EditMode 测试，并在文档里如实标注 |

### Step 9 · 预检与诊断报告

- [x] 预检项：空画布、重名资源、缺失字体、超尺寸图、无类型标签的疑似控件、九宫歧义、被忽略的整棵子树
- [x] 报告模型与落盘（记录解析耗时、节点数、导出资源数、诊断明细）
- **验收**：构造的问题 PSD 能产出预期诊断；正常 PSD 无 Error 级诊断
- **验收结果**：`dotnet test` 343 项全绿（本步 +22）；真实样本预检出 0 Error / 2 Warning / 14 Info，
  报告落盘 `/tmp/psd9_report.json`（见下）
- **提交**：`feat(editor): 预检与诊断报告`

**这一版怎么做的**

| 部件 | 位置 | 职责 |
| --- | --- | --- |
| `Preflight` | `Runtime/Core/Pipeline` | 七项检查，只产出 `preflight.*` 诊断，不改任何数据 |
| `PreflightOptions` | 同上 | 已知字体表、贴图上限、是否提示「猜出来的控件类型」 |
| `NodeBuilder` 的类型来源标记 | `Runtime/Core/Semantics` | 每个节点记 `Tags["type-source"] = tag / override / inferred`，预检靠它区分「标签钉死的」与「按名字猜的」 |
| `ImportReport` | `Runtime/Core/Pipeline` | 体检报告：来源与体积、解析/计划耗时、节点/资源/引用计数、诊断明细、语义层统计；`ToJsonText` 直接落盘 |
| `EditorPreflight` | `Editor/Import` | 编辑器侧入口：把 AssetDatabase 里的字体名与工程贴图上限喂给 Core 的检查 |

预检项与判据：

| 诊断码 | 级别 | 触发条件 |
| --- | --- | --- |
| `preflight.empty-canvas` | Error | 画布宽或高 ≤ 0，生成出来必然不可用 |
| `preflight.empty-document` | Warning | 画布里一个图层都没有 |
| `preflight.duplicate-sprite-name` | Warning | 多个**内容不同**的图层同名，`ref <名字>` 会取到意外的那张 |
| `preflight.oversized-sprite` | Warning | 贴图边长超过工程 Max Size（会静默缩小） |
| `preflight.font-missing` | Warning | 文字图层用的字体不在工程里（会退回默认字体） |
| `preflight.font-unknown` | Info | 设计稿里没写字体信息 |
| `preflight.slice-degenerate` | Warning | 九宫边框把中间可拉伸区切没了 |
| `preflight.ignored-subtree` | Warning | 被 `ignore` 的图层还带着子图层（会一起消失） |
| `preflight.inferred-control` | Info | 按钮/开关/滑条这类**有行为**的控件类型是按名字猜的，没有标签钉死 |

**验收结果**

- `dotnet test Tools~/CoreTests` **343 项全绿**（Step 8 是 321，本步 +22，全部落在 `PreflightTests`）：
  正常文档零诊断、空画布报错、空文档提示、重名资源（含「内容相同不算重名」）、
  九宫退化三态（4x4 切 2 / 5x5 切 2 / 6x6 切 2）、超尺寸与不超尺寸、字体缺失/命中/未填写/不传字体表、
  忽略子树（有子节点才提示）、猜出来的控件类型（标签指定与图片类型都不提示）、
  语义层类型来源标记、报告计数与 JSON 往返。
- Editor 脚本编译 0 错误 0 警告（新增 `EditorPreflight`）。
- 真实样本 `Psd2UguiForm.psd`：
  `PsdDump Psd2UguiForm.psd --module common --preflight --report /tmp/psd9_report.json`
  → 0 Error / 2 Warning / 14 Info，警告是「Mark、ToggleBg 两个图层重名」与 2 条
  「ButtonBlack、ButtonBule 的 tmp-button 类型是按名字猜的」；报告里 `nodes=52 sprites=12
  slicedSprites=6 sharedSprites=3 referencesLocal=4`、`parseMs≈20 planMs≈13`。

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 九宫预检在真实样本上刷出 6 条假警报 | 判据写成「边框加起来超过边长一半」，但「中间只剩 1 像素」恰恰是描边块最常见也最正确的写法 | 只在中间区**完全消失**（`左+右 ≥ 宽`）时才报，真实样本的假警报归零 |
| 忽略子树的提示数字多算一个 | `CountDescendants()` 把自己也算进去 | 减一，并把这条规则写进注释 |
| 同一批诊断被写了两遍 | `--export-dir` 与 `--prefab-plan` 各自跑了一次导出计划 | PsdDump 改成共享一份 `ExportPlan`（顺带让报告里的统计与诊断是同一份） |
| 想提示「这个控件类型是猜的」却查不出来 | 语义层只记总计数（`Stats["inferred"]`），没在节点上留来源 | 每个节点记 `Tags["type-source"]`，标签/覆盖表/推断三条路径分别落值 |

### Step 10 · 编辑器工作流

- [x] `Psd2UguiWindow`：拖入 PSD、节点树视图、类型/角色覆盖、选项面板、生成与报告区
- [x] 菜单与右键入口：`Assets/PSD2UGUI/…`（解析、导出资源、生成 Prefab、重新生成）
- [x] 批处理 API：`Psd2UguiPipeline.Run(options)`，可用于 CI / `-executeMethod`
- **验收**：窗口可完成全流程；批处理 API 在 EditMode 测试中跑通
- **验收结果**：Editor 脚本编译 0 错误 0 警告；`dotnet test` 349 项全绿（+6 覆盖表写入端）。
  本机 Unity 起不了无头实例，窗口与批处理的**运行时**跑通顺延到 Step 11，
  这里先说清手动核对步骤与命令行用法（见下）
- **提交**：`feat(editor): 编辑器窗口与一键生成工作流`

**这一版怎么做的**

| 部件 | 位置 | 职责 |
| --- | --- | --- |
| `Psd2UguiPipeline` | `Editor/` | 唯一的流程编排：解析（含覆盖表）→ 跨界面复用 → 导出计划 → 预检 → 贴图落盘 → 契约 → 预制体（增量）→ 体检报告 |
| `Psd2UguiRunOptions` / `Psd2UguiRunResult` | 同上 | 参数与结果模型，窗口、菜单、批处理共用同一份 |
| `Psd2UguiWindow` | `Editor/` | 拖入 PSD、选项面板、节点树、类型/角色覆盖、诊断与报告区 |
| `Psd2UguiMenu` | `Editor/` | `Assets/PSD2UGUI/…` 右键入口 + `Window/PSD2UGUI/…`，未选中 PSD 时自动置灰 |
| `Psd2UguiBatch` | `Editor/Batch/` | `-executeMethod` 批处理：`-psd2ugui-psd`（可重复）/`-psd2ugui-psdDir`/`-psd2ugui-module`/`-psd2ugui-no-prefab`，按失败个数设置退出码 |
| `NodeOverrides.Set/Remove/ToJsonText` | `Runtime/Core/Semantics` | 覆盖表的写入端：窗口改完类型/角色写回 `<AssetRoot>/overrides/<模块>/<源>.overrides.json` |

窗口里的「写入覆盖表」用 `#<图层ID>` 作键：图层改名不会失效；改完立刻重解析，能马上看到效果。

**手动核对步骤（本机没法跑 Unity，留给能跑的环境）**

1. 打开 `Window/PSD2UGUI/导入窗口`，把 `Psd2UguiForm.psd` 拖进「源 PSD」，点「只解析」：
   左边出现 52 个节点的树，右边显示选中节点的类型来源（标签 / 覆盖表 / 猜的）。
2. 选中一个 `inferred` 的按钮 → 类型改成 `button` → 「写入覆盖表」→ 树里的类型来源变成「覆盖表」；
   再点「一键生成 Prefab」→ `Assets/PSD2UGUI/prefab/common/Psd2UguiForm.prefab` 出现，
   报告区显示节点数、贴图数与 E/W/I 计数，「打开报告」能定位到 `.report.json`。
3. 手动改预制体里某个按钮的颜色 → 再点「一键生成 Prefab」→ 改动仍在（Step 8 的增量更新）。
4. 批处理（CI）：
   `Unity -batchmode -quit -projectPath <项目> -executeMethod Psd2Ugui.Editor.Batch.Psd2UguiBatch.Run
   -psd2ugui-module common -psd2ugui-psdDir Assets/UI` → 退出码 0 表示全部成功。

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 批处理加 `-psd2ugui-no-prefab` 后每个文件都算失败 | 成功判定写成「必须产出预制体」 | 按「这次期望的产物」判定：只导出时看文档与导出计划是否跑通 |
| 点「只解析」也会往磁盘写契约 JSON | `WriteContract` 跟着开关走，而不是跟着产物走 | 「只解析」不写任何文件（报告与契约只在真正出产物时写） |
| 节点树里的分组节点点不中 | 折叠三角和「选中」挤在同一个 `Foldout` 上 | 三角只负责展开/收起，名字单独做成可点按钮 |
| 窗口、菜单、批处理三处参数容易走偏 | 各写各的流程 | 全部收敛到 `Psd2UguiPipeline.Run(Psd2UguiRunOptions)`，窗口只收集参数、菜单只传路径 |

### Step 11 · Unity 测试与无头验证

- [x] `Tests/` 程序集：导出、装配、增量、路径约定的 EditMode 测试
- [x] 自造 PSD fixture（测试内生成的 PSD 字节流），不依赖外部素材
- [x] `Tools~/dev-project.sh`：生成宿主工程、把本包装进去、无头运行 EditMode 测试
- [x] 真实 PSD 冒烟验证（本地样本，不入库；用环境变量指过去）
- **验收**：`Unity -batchmode -runTests -testPlatform EditMode` 全绿
- **验收结果**：本机 Unity 2022.3.62f3 **真的跑了**：13 个用例 0 失败（其中真实 PSD 冒烟默认跳过，
  没配样本时是 12 通过 + 1 跳过）；配上样本 `Psd2UguiForm.psd` 后 13/13 全过，
  冒烟产出「53 节点 / 12 张图 / E0」。`bash Tools~/run-tests.sh` 五项全绿（dotnet 349 + EditMode 13）。
  **改口说明**：Step 10 曾判断「本机 Unity 起不了无头实例」，这次实测是能起的——
  当时多半撞上了授权握手抖动，正解是照常 `-batchmode` 跑，只是不能加 `-quit`（见坑表）。
- **提交**：`test: EditMode 测试与无头验证脚本`

**这一版怎么做的**

| 部件 | 位置 | 职责 |
| --- | --- | --- |
| `PsdFixtureBuilder` | `Tests/Fixture/` | 直接拼 PSD 二进制：图层记录、通道数据、`lsct` 分组、图层 ID，不依赖任何外部素材 |
| `Psd2Ugui.Tests.Editor.asmdef` | `Tests/` | Editor-only 测试程序集；`defineConstraints: UNITY_INCLUDE_TESTS`，靠宿主工程 `testables` 打开 |
| `Psd2UguiPipelineTests` | `Tests/Editor/` | 端到端：真写 PSD 字节 → 真导出贴图 → 真存预制体 → 真重新导出一遍 |
| `Psd2UguiPathsTests` | `Tests/Editor/` | 产物路径约定（算错就是「生成了但找不到」） |
| `Tools~/dev-project.sh` | `Tools~/` | 建宿主工程 → 装包 → 无头跑 EditMode；支持 `--sync` / `--create-only` / `--clean-meta` |
| `NUnitShim` | `Tools~/EditorCompile/Shims/` | 让「不开编辑器」也能把 `Tests/**` 编译一遍；只补 API 形状，不实现行为 |
| `run-tests.sh` | `Tools~/` | 5 步：Core 测试 → netstandard2.1 编译 → Editor+Tests 编译 → PsdDump → Unity EditMode |

夹具从 `Tools~/CoreTests/` **搬到了 `Tests/Fixture/`**，dotnet 测试用 `<Compile Include>` 链过去：
一份源码喂两个测试程序集，Core 侧规则与 Unity 侧行为不会各说各话。

**测试覆盖了什么**

| 用例 | 钉住的行为 |
| --- | --- |
| 一键生成产出贴图契约报告与预制体 | 窗口/菜单/批处理共用的那条主流水线端到端可用；节点上挂着 `Psd2UguiNode`；标签决定控件 |
| 重新导出保留人工改动 | 手工改的颜色、手工加的子节点，重新导出后还在（增量更新的命门） |
| 设计稿删掉的图层会从预制体里移除 | 增量不是只会加，删掉的图层要真的从 Prefab 里清掉 |
| 重复导出不会把组件挂两遍 | 幂等：同一份设计稿连导两次，`Image`/`Button` 不会叠 |
| 共享贴图跨界面复用不重复落盘 | `ref Logo.img` 引用别的界面导过的图，目录里不多出文件，`Image.sprite` 指向共享资产 |
| 覆盖表写进文件后重新导出就生效 | `overrides/<模块>/<源>.overrides.json` 真的参与解析，`type-source` 变成 `override` |
| 预检诊断会写进报告 | 重名资源这类问题会进 `.report.json`，不是只在控制台一闪 |
| 批处理遇到坏路径返回失败个数 | `RunAll` 的失败计数与退出码约定 |
| 产物必须落在 Assets 下 / 路径约定 / 模块留空 | 路径算错会「生成了但找不到」，这几条锁死 |
| 真实 PSD 冒烟（可选） | 配 `PSD2UGUI_SMOKE_PSD=<样本.psd>` 时，用真实设计稿跑通全流程且无 Error 级诊断 |

**怎么跑**

```bash
bash Tools~/run-tests.sh                 # 全部五项（含 Unity EditMode）
Tools~/dev-project.sh                    # 只跑 Unity 侧
Tools~/dev-project.sh --sync             # 改完代码只同步包，不重建工程
PSD2UGUI_SMOKE_PSD=/path/to/xxx.psd Tools~/dev-project.sh   # 带真实 PSD 冒烟
SKIP_UNITY_TESTS=1 bash Tools~/run-tests.sh                 # CI 里只跑 dotnet
```

`dev-project.sh` 把包**复制**到宿主工程里，而不是软链回仓库：Unity 会给包内每个资产补 `.meta`，
软链等于往仓库里灌一堆未跟踪文件；复制过去则让 Unity 去折腾那份副本。
仓库目前仍然一个 `.meta` 都没入库（既有约定，长期看应该补上，见「已知遗留」）。

**踩过的坑（写下来避免以后重复踩）**

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 测试「全部通过」，但一个用例都没跑 | 同时传了 `-quit`：`-runTests` 要靠 `delayCall` 启动，`-quit` 在那之前就把编辑器关了；退出码还是 0 | 去掉 `-quit`；并且**必须**校验 `-testResults` 文件真的落盘，只看退出码会骗人 |
| 离线编译检查报 `LogAssert` 不存在 | 测试里少一句 `using UnityEngine.TestTools;`——在 Unity 里同样是错的 | 补上；顺带证明「把 `Tests/**` 纳进编译检查」不是白做的 |
| 第一次导出报 `DirectoryNotFoundException: .../manifest/common/X.psd2ugui.json` | `SpriteExporter` 只建了贴图目录，而 manifest 与贴图不在同一层 | 写盘前把 manifest 目录一起建出来 |
| `MissingReferenceException: RectTransform has been destroyed` | 把 `LoadPrefabContents` 出来的对象带出了 `UnloadPrefabContents` 作用域 | 测试里只允许在回调内使用（`OnPrefab`）；`Assert.IsNotNull(已销毁对象)` 还会假过 |
| 脚本报 `code，: unbound variable` | bash 把紧跟 `$VAR` 的全角标点算进了变量名 | 中文提示里的变量一律写 `${VAR}` |
| 冒烟里诊断比命令行多一大截（W39/I28 vs W2/I14） | 命令行不知道工程里有哪些字体、也没装 TMP | 属于预期：`preflight.font-missing` ×21、`text.tmp-unavailable` ×13 正是「降级并被报出来」；冒烟用例会打一张诊断分布表 |

### Step 12 · 文档与收尾

- [ ] `docs/ARCHITECTURE.md`、`docs/CONTRACT.md`、`docs/USAGE.md`、`docs/LIMITATIONS.md`
- [ ] `README.md` 完善（安装、快速开始、参数说明、FAQ）
- [ ] `CHANGELOG.md`，版本 `0.1.0`
- **验收**：新用户按文档可独立完成一次 PSD → Prefab；限制说明与实现一致
- **提交**：`docs: 完善使用与架构文档，发布 0.1.0`

---

## 4. 全局验收标准

1. `dotnet test Tools~/CoreTests` 全绿。
2. `Unity -batchmode -runTests -testPlatform EditMode` 全绿（本机 Unity 2022.3.62f3）。
3. 端到端：真实 PSD → 生成 Prefab，层级/文本/九宫正确，无 Error 级诊断。
4. 增量：生成 → 手工调整 → 重新生成，人工改动保留。
5. 零第三方运行时依赖；`Runtime/Core` 不引用 `UnityEngine`。

## 5. 风险与对策

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| Unity 无可用授权导致无头测试失败 | 无法自动验收 | 先跑 dotnet 测试保证 Core 正确；`Tools~/dev-project.sh` 把「环境不满足」与「用例失败」分成退出码 3 与 2，前者不阻塞整条流水线 |
| PSD 特性覆盖不全（16bit、CMYK、智能对象、矢量蒙版、图层组混合模式） | 还原偏差 | 分级支持：能解析的正确解析，不能解析的产出诊断而不是静默错误 |
| TMP 未安装或未导入 Essentials | 文本效果缺失 | asmdef `versionDefines` 条件编译 + 运行时检测，降级到 UGUI Text 并给诊断 |
| 增量更新误删用户改动 | 用户资产受损 | 节点归属标记 + 身份映射 + 只管理自己产物；破坏性操作前备份到临时目录 |
| 自研解析器在大 PSD 上性能不足 | 解析慢 | 通道按需解码、位图延迟合成、解析耗时写入报告 |

## 6. 提交记录

| 步骤 | 提交信息 | 状态 |
| --- | --- | --- |
| Step 0 | `chore: 初始化 PSD2UGUI 仓库与任务清单` | ✅ |
| Step 1 | `feat(core): 契约数据模型与 JSON 写出` | ✅ |
| Step 2 | `feat(core): 自研 PSD 二进制解析器` | ✅ |
| Step 3 | `feat(core): 图层树构建与位图合成` | ✅ |
| Step 4 | `feat(core): 图层语义标签与控件类型推断` | ✅ |
| Step 5 | `feat(core): 自动九宫检测与 PNG 编码器` | ✅ |
| Step 6 | `feat(editor): Sprite 导出与导入设置` | ✅ |
| Step 7 | `feat(editor): uGUI Prefab 装配与文本效果` | ✅ |
| Step 8 | `feat(editor): 增量更新与资源复用` | ✅ |
| Step 9 | `feat(editor): 预检与诊断报告` | ✅ |
| Step 10 | `feat(editor): 编辑器窗口与一键生成工作流` | ✅ |
| Step 11 | `test: EditMode 测试与无头验证脚本` | ✅ |
| Step 12 | `docs: 完善使用与架构文档，发布 0.1.0` | ⬜ |

---

## 7. 已知遗留

| 项 | 现状 | 影响 | 打算怎么办 |
| --- | --- | --- | --- |
| 仓库里没有任何 `.meta` 文件 | 所有 `.cs` / `.asmdef` 都没有入库；Unity 打开后会自己补 | 别人 `git clone` 后拿到的 GUID 与本机不一致，Prefab / 材质上的引用可能对不上；作为 UPM 包分发时也不规范 | 发版前跑一次 Unity 让编辑器生成，再把 `*.meta` 一起提交（这是 Unity 包的常规做法） |
| 真实 PSD 冒烟依赖本机样本 | 样本有体积与授权问题，不入库 | 默认只跑自造字节流的 12 个用例 | 需要时用 `PSD2UGUI_SMOKE_PSD=<样本.psd>` 指过去 |
| TMP 分支未在真实 TMP 工程里验证 | 宿主工程没导 TMP Essentials，走的是 `text.tmp-unavailable` 降级路径 | 文本落在 UGUI Text 上；TMP 路径的控件装配只过了编译检查 | 在装了 TMP 的工程里跑一次 `dev-project.sh`，或手工核对 |
| 16bit / CMYK / 智能对象 / 矢量蒙版 | 解析器按分级支持处理，遇到不认识的会出诊断而不是静默出错 | 个别设计稿还原度不够 | 按真实报错逐条补，诊断里已经能定位到图层 |
