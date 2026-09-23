# PSD2UGUI

在 Unity 端导入 PSD，解析图层树，一键生成可继续编辑的 uGUI Prefab。

> 状态：开发中。当前进度见 [docs/TASKS.md](docs/TASKS.md)。

## 目标

把 PSD 里已经写好的布局、文字、颜色、字号、描边、阴影和九宫直接变成 uGUI 结构，
让美术不必反复标注，让程序不必手工拼界面。

## 特性规划

- 自研 PSD 解析器：不依赖 Aspose / Newtonsoft 等第三方库，无水印、无授权风险
- 图层树 → 可编辑节点树 → Prefab，支持生成前人工调整类型与层级
- 命名标签 + 启发式推断自动识别控件类型，识别不准时可手动覆盖
- 自动九宫检测、图片 / Prefab 复用、重新导出增量更新
- 文本样式同步（字号、颜色、描边、阴影、渐变）
- 无 UnityEngine 依赖的 Core 层，可脱离 Unity 直接单元测试

## 文档

- [任务清单](docs/TASKS.md)
- [架构设计](docs/ARCHITECTURE.md)
- [使用说明](docs/USAGE.md)
- [已知限制](docs/LIMITATIONS.md)

## 许可

[MIT](LICENSE)
