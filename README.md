# PSD2UGUI

在 Unity 端导入 PSD，解析图层树，一键生成**可以继续编辑**的 uGUI Prefab。

- 美术不必再逐个标注，程序不必再手工拼界面
- 生成的 Prefab 是普通的 uGUI 结构，没有任何运行时依赖，也没有私有组件
- 重新导出时**只覆盖工具自己管的属性**，手加的节点、手改的颜色都留着
- 解析器、PNG 编码、JSON 全部自带，**零第三方依赖**

```text
PSD ──► 解析 ──► 契约（可读 JSON）──► 贴图 + Prefab
                    ▲
                 人工覆盖表 / 图层名标签在这里介入
```

## 安装

`Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.sicy.psd2ugui": "https://github.com/<你的账号>/Psd2UGUI.git"
  }
}
```

本地包则写 `"com.sicy.psd2ugui": "file:../../Psd2UGUI"`。

要求：Unity 2021.3+。TextMeshPro 可选——没装也能跑，文本会自动降级成 uGUI `Text` 并给一条提示。

## 快速开始

1. 把 `.psd` 拖进工程
2. 在 Project 里选中它
3. 右键 → `PSD2UGUI/一键生成 Prefab`（`Ctrl/Cmd + Shift + G`）
4. 去 `Assets/PSD2UGUI/prefab/<模块>/` 取 Prefab

想改控件类型、看诊断、调选项，用 `Window → PSD2UGUI → 导入窗口`。

## 图层怎么命名

在图层名后面跟点号加标签：

```text
ButtonBule.btn          按钮
Icon.img.bg             某控件的背景图
Arrow.img.dpdicon       下拉框的箭头
Panel.panel
Dialog.bt.refp          复用已生成的 Dialog 子 Prefab
Bg9.sliced.img          按九宫格导出
Draft.ignore            整棵子树不导出
```

不写标签也能跑——解析器会看文本、分组、纯色这些结构性事实，再按名字关键词猜，
每条推断都标明来源是「标签 / 覆盖表 / 猜的」，猜错了可以用覆盖表钉死。

完整标签表与覆盖表写法见 **[docs/TAGS.md](docs/TAGS.md)**。

## 产物

```text
Assets/PSD2UGUI/
├─ sprite/<模块>/       PNG
├─ prefab/<模块>/       .prefab
├─ contract/<模块>/     .contract.json    解析结果的可读快照
├─ manifest/<模块>/     .psd2ugui.json    导出清单（增量与清理用）
├─ report/<模块>/       .report.json      体检报告
└─ overrides/<模块>/    .overrides.json   人工覆盖表
```

## 文档

| 文档 | 内容 |
| --- | --- |
| [使用说明](docs/USAGE.md) | 安装、快速开始、菜单、选项、覆盖表、增量更新、CI、代码调用、FAQ |
| [图层命名与覆盖表](docs/TAGS.md) | 给美术的一页纸：标签怎么写、推断错了怎么改 |
| [契约格式](docs/CONTRACT.md) | 中间 JSON 的字段含义与版本策略 |
| [架构设计](docs/ARCHITECTURE.md) | 分层、数据流、关键设计决策、扩展点 |
| [已知限制](docs/LIMITATIONS.md) | 做不到什么、会怎么降级，以及报问题要带什么 |
| [更新日志](CHANGELOG.md) | 版本变化 |
| [任务清单](docs/TASKS.md) | 开发进度与每步的实现说明 |

## 开发

```bash
bash Tools~/run-tests.sh          # 全部检查：Core 单测 + 编译检查 + Unity EditMode 测试
Tools~/dev-project.sh             # 只跑 Unity 侧（自动生成宿主工程）
Tools~/dev-project.sh --sync      # 改完代码只同步包内容
PSD2UGUI_SMOKE_PSD=/path/to/x.psd Tools~/dev-project.sh   # 带真实 PSD 冒烟
```

不开编辑器排查某个 PSD：

```bash
dotnet run --project "Tools~/PsdDump" -- path/to/file.psd --nodes
dotnet run --project "Tools~/PsdDump" -- path/to/file.psd --json /tmp/contract.json
dotnet run --project "Tools~/PsdDump" -- path/to/file.psd --preflight --report /tmp/report.json
```

与 Python 的 psd-tools 交叉校验图层明细（详见 `Tools~/psd-tools-verify/README.md`）：

```bash
dotnet run --project "Tools~/PsdDump" -- path/to/file.psd --layers /tmp/actual.json
python3 Tools~/psd-tools-verify/compare.py /tmp/reference.json /tmp/actual.json
```

`Tools~/` 以 `~` 结尾，Unity 不会编译它，里面只放开发期的测试与命令行工具。

## 状态

版本 `0.1.0`。核心流程（解析 → 资源 → Prefab → 增量更新）已跑通并有测试覆盖，
已知缺口集中在**还原度**上：图层混合模式不参与合成、调整层不应用、
智能对象只使用内嵌预览像素。动手前建议先看 [已知限制](docs/LIMITATIONS.md)。

## 许可

[MIT](LICENSE)
