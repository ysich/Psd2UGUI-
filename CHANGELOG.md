# 更新日志

版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。
`0.x` 阶段接口仍可能变动，破坏性改动会在下方标出来。

## [0.1.0] - 2026-09-24

首个可用版本。目标：**Unity 端导入 PSD → 生成可继续编辑的 uGUI Prefab**。

### 新增

**解析**

- 自研 PSD / PSB 二进制解析器，无第三方依赖：文件头、颜色模式、图像资源、
  图层与蒙版、附加信息块、合成图像
- 位深 1 / 8 / 16 / 32；颜色模式 RGB、灰度、位图、CMYK（近似）；索引色（按 RGB 通道）
- 通道压缩：原始、RLE、ZIP、ZIP + 差分预测
- 图层组（含折叠状态）、图层蒙版、裁剪图层、图层 ID（`lyid`，缺失时按顺序补编号）
- 文本图层：内容、字号、颜色、字体名、对齐、行距、字距、点/段落文本
- 十种图层效果：投影、内阴影、外发光、内发光、斜面和浮雕、颜色叠加、渐变叠加、描边、缎面、图案叠加

**语义**

- 图层名标签体系：控件类型（`img` / `btn` / `tmptxt` / `sv` …）、
  子节点角色（`bg` / `fill` / `handle` / `template` …）、开关（`sliced` / `ignore` / `ref` / `refp`）
- 启发式类型推断：文本 → 分组 → 纯色 → 名字关键词 → 有像素即图片
- 人工覆盖表：按名字 / `#图层ID` / 完整路径匹配，可改类型、角色、名字、层级、
  九宫、忽略整棵子树、指定复用资源
- 每个节点记录类型结论的来源（`tag` / `override` / `inferred`），可追溯

**资源**

- 自动九宫检测；资源文件名带内容哈希，内容没变就不重写文件
- `ref` 跨界面、跨模块复用同一张图，不重复落盘
- 失效资源清理（被别的界面引用的会跳过）
- 自研 PNG 编码器（DEFLATE 直接用 .NET 内置压缩）

**Prefab**

- 图层树 → uGUI 结构：Image / RawImage / Text / TMP Text / Panel / Mask /
  Button / Toggle / Slider / ScrollView / Dropdown / InputField / List / Grid 等
- 复合控件按 uGUI 标准结构补齐缺失零件（Viewport、Content、Handle、Template…），补齐时会报诊断
- 文本效果映射：描边与外发光 → Outline，投影 → Shadow，TMP 渐变 → 顶点渐变
- **增量更新**：按稳定节点 ID 认领已有对象，只覆盖工具管的属性，
  人工改动一律保留；设计稿删掉的图层才会被移除
- 根节点可选带 Canvas + CanvasScaler + GraphicRaycaster

**编辑器体验**

- `Window/PSD2UGUI/导入窗口`：拖入 PSD、节点树、类型/角色覆盖、选项面板、诊断与报告
- 右键菜单 `Assets/PSD2UGUI/…`：一键生成（`Ctrl/Cmd+Shift+G`）、只导出资源、
  只解析、批量生成同目录、打开窗口
- 批处理入口 `Psd2Ugui.Editor.Batch.Psd2UguiBatch.Run`，按失败个数返回退出码
- 预检：空画布、重名资源、超大贴图、缺字体、退化九宫、被忽略的子树
- 体检报告 `.report.json`：输入、耗时、产物数量、全部诊断
- 所有诊断带 `code` 与图层路径，窗口里可直接点过去

### 测试

- 349 个 dotnet 单元测试（Core 层，不依赖 Unity）
- 13 个 Unity EditMode 测试：端到端跑「真写 PSD 字节 → 真导出贴图 → 真存预制体 → 真重新导出一遍」，
  另覆盖路径约定、覆盖表生效、跨界面复用、批处理失败计数；
  可选的真实 PSD 冒烟用 `PSD2UGUI_SMOKE_PSD=<样本.psd>` 打开
- `Tools~/run-tests.sh` 一条命令跑完全部五项（Core 测试 → netstandard2.1 编译 →
  Editor 与 Tests 编译 → 命令行工具 → Unity EditMode）
- `Tools~/PsdDump`：不开编辑器就能看图层树、导契约、跑预检、出报告

### 已知限制

见 [docs/LIMITATIONS.md](docs/LIMITATIONS.md)。最需要注意的几条：

- **图层混合模式不参与合成**（只有普通 alpha 叠加），带 `Multiply` / `Screen` 等的设计稿颜色会偏
- 调整层不应用；智能对象只使用内嵌预览像素
- 图片节点上的图层效果无法用 uGUI 表达，会报 `effect.not-applied` 并忽略
- 仓库暂未提交 `.meta` 文件，作为 UPM 包分发前需要补上
