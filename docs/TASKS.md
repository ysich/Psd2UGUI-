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
├─ Runtime/Core/                   # 纯 C#，零 UnityEngine 依赖（可 dotnet 测试）
│  ├─ Contract/                    # 中间契约：文档、节点、资源、诊断、JSON 写出
│  ├─ Psd/                         # PSD 二进制解析器（PSD/PSB、RLE/ZIP）
│  ├─ Imaging/                     # 位图、裁剪/去空、PNG 编码、九宫检测
│  ├─ Semantics/                   # 标签解析、类型推断、角色分配、覆盖表
│  └─ Pipeline/                    # 编排、选项、预检
├─ Editor/                         # Unity Editor 侧
│  ├─ Import/                      # 读取 PSD、Sprite 落盘、导入设置、资源复用
│  ├─ Build/                       # Prefab 装配、控件工厂、角色连线、TMP 效果、增量更新
│  ├─ Reports/                     # 导出报告
│  └─ Ui/                          # 编辑器窗口、节点树视图、菜单、批处理入口
├─ Tests/                          # Unity EditMode 测试（NUnit + asmdef）
├─ Tools~/                         # Unity 忽略目录
│  ├─ CoreTests/                   # dotnet 测试工程（直接编译 Runtime/Core 源码）
│  └─ dev-project.sh               # 生成宿主工程并无头运行 EditMode 测试
└─ docs/                           # 任务清单、架构、契约、使用、限制
```

**程序集划分**

| 程序集 | 依赖 | 说明 |
| --- | --- | --- |
| `Psd2Ugui.Core` | 无 | 解析、契约、位图、PNG、语义推断，纯 C# |
| `Psd2Ugui.Editor` | Core、UnityEditor、UGUI；TMP 用 versionDefines 可选 | 资源导出与 Prefab 装配 |
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

- [ ] 标签解析：`Name.btn` / `Name.text` / `Name.tmp` / `Name.img` / `Name.rimg` / `Name.sv` / `Name.sld` / `Name.tg` / `Name.ipt` / `Name.dpd` / `Name.panel` / `Name.mask` / `Name.color`
- [ ] 角色标签：`bg` / `onover` / `press` / `select` / `disable` / `bttxt` / `fill` / `handle` / `vpt` / `hbar` / `vbar` / `dpdicon` / `placeholder` / `ipttxt` / `mark` / `tglb` / `ignore`
- [ ] 启发式推断：无标签时按图层类型/名称/结构/尺寸推断控件与角色
- [ ] 覆盖表：JSON 形式的 `overrides` 支持人工指定类型与层级
- [ ] `ITypeInferrer` 接口（预留 AI 推断实现位）
- **验收**：标签用例表与推断用例表全部通过；误判可被覆盖表修正
- **提交**：`feat(core): 图层语义标签与控件类型推断`

### Step 5 · 九宫检测与 PNG 编码

- [ ] 九宫检测：Alpha/边缘一致性扫描，输出 `left/bottom/right/top` 与最小可拉伸图裁剪
- [ ] 自研 PNG 编码器（zlib/Deflate + CRC32 + Adler32，RGBA32）
- [ ] 与 Unity 侧无耦合，可独立测试
- **验收**：编码结果可被系统图片工具正确解码；九宫用例（固定边框、渐变边框、纯色、无边框）判定正确
- **提交**：`feat(core): 自动九宫检测与 PNG 编码器`

### Step 6 · 资源导出与导入设置（Editor）

- [ ] `SpriteExporter`：裁剪 → 去空 → 落盘 PNG → `TextureImporter` 配置（Sprite、Pivot、九宫 Border、关 mipmap/压缩策略）
- [ ] 资源复用注册表：内容哈希 + 稳定 ID → 复用已有资源，避免重复导出
- [ ] 契约 JSON 与身份映射文件落盘（`.psd2ugui.json` / 身份表）
- [ ] 路径规划：`Assets/<输出根>/<模块>/<资源名>.png`，重名冲突诊断
- **验收**：导出目录结构符合规划；重复导出不产生重复资源；导入设置断言通过
- **提交**：`feat(editor): Sprite 导出与导入设置`

### Step 7 · Prefab 装配（Editor）

- [ ] `RectTransform` 布局：PSD 像素 → 左上锚点布局，根节点画布尺寸、可选居中/适配
- [ ] 控件工厂：Image、RawImage、Text、TMP Text、Button、Toggle、InputField、Slider、ScrollView、Dropdown、Panel、Mask、纯色
- [ ] 角色连线：`targetGraphic`、`fillRect`、`handleRect`、`viewport`、`content`、`placeholder`、状态图切换
- [ ] 文本样式：字号/颜色/对齐/字体路由；描边、阴影、渐变（TMP 材质实例化并落盘；UGUI Text 用组件实现）
- [ ] 生成 `Psd2UguiNode` 标记组件（记录稳定 ID 与来源图层）
- **验收**：EditMode 测试断言层级、组件、关键属性与九宫 Border 正确
- **提交**：`feat(editor): uGUI Prefab 装配与文本效果`

### Step 8 · 增量更新与资源复用

- [ ] 重新导出时按稳定 ID 匹配：更新属性、新增节点、移除消失节点
- [ ] 保留人工改动：用户新增的子节点/组件不被删除，被工具管理的属性才覆盖
- [ ] `ref` / `refp`：跨界面共享图片与子 Prefab 复用
- [ ] 冲突与失效诊断（资源被占用、Prefab 被改动等）
- **验收**：EditMode 测试「生成 → 手动改 → 重新生成」后人工改动仍在，且新增/删除正确
- **提交**：`feat(editor): 增量更新与资源复用`

### Step 9 · 预检与诊断报告

- [ ] 预检项：空画布、重名资源、缺失字体、超尺寸图、无类型标签的疑似控件、九宫歧义、被忽略的整棵子树
- [ ] 报告模型与落盘（记录解析耗时、节点数、导出资源数、诊断明细）
- **验收**：构造的问题 PSD 能产出预期诊断；正常 PSD 无 Error 级诊断
- **提交**：`feat(editor): 预检与诊断报告`

### Step 10 · 编辑器工作流

- [ ] `Psd2UguiWindow`：拖入 PSD、节点树视图、类型/角色覆盖、选项面板、生成与报告区
- [ ] 菜单与右键入口：`Assets/PSD2UGUI/…`（解析、导出资源、生成 Prefab、重新生成）
- [ ] 批处理 API：`Psd2UguiPipeline.Run(options)`，可用于 CI / `-executeMethod`
- **验收**：窗口可完成全流程；批处理 API 在 EditMode 测试中跑通
- **提交**：`feat(editor): 编辑器窗口与一键生成工作流`

### Step 11 · Unity 测试与无头验证

- [ ] `Tests/` 程序集：解析、语义、导出、装配、增量的 EditMode 测试
- [ ] 自造 PSD fixture（测试内生成的 PSD 字节流），不依赖外部素材
- [ ] `Tools~/dev-project.sh`：生成宿主工程、软链接/引用本包、无头运行 EditMode 测试
- [ ] 真实 PSD 冒烟验证（本地样本，不入库）
- **验收**：`Unity -batchmode -runTests -testPlatform EditMode` 全绿
- **提交**：`test: EditMode 测试与无头验证脚本`

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
| Unity 无可用授权导致无头测试失败 | 无法自动验收 | 先跑 dotnet 测试保证 Core 正确；Unity 侧退化为「编译 + 手动验证说明」，并在交付说明中如实标注 |
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

| Step 4 | `feat(core): 图层语义标签与控件类型推断` | ⬜ |
| Step 5 | `feat(core): 自动九宫检测与 PNG 编码器` | ⬜ |
| Step 6 | `feat(editor): Sprite 导出与导入设置` | ⬜ |
| Step 7 | `feat(editor): uGUI Prefab 装配与文本效果` | ⬜ |
| Step 8 | `feat(editor): 增量更新与资源复用` | ⬜ |
| Step 9 | `feat(editor): 预检与诊断报告` | ⬜ |
| Step 10 | `feat(editor): 编辑器窗口与一键生成工作流` | ⬜ |
| Step 11 | `test: EditMode 测试与无头验证脚本` | ⬜ |
| Step 12 | `docs: 完善使用与架构文档，发布 0.1.0` | ⬜ |
