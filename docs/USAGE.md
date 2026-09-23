# 使用说明

## 1. 环境要求

| 项 | 要求 |
| --- | --- |
| Unity | 2021.3 及以上（开发与验证用的是 2022.3.62f3） |
| uGUI | `com.unity.ugui`，Unity 自带 |
| TextMeshPro | 可选。没装也能跑，文本会降级成 UGUI `Text` 并给一条 `text.tmp-unavailable` 提示 |
| 第三方库 | 无。解析器、PNG 编码、JSON 都是本包自带 |

## 2. 安装

**方式一：UPM Git 引用**（推荐）

`Packages/manifest.json`：

```json
{
  "dependencies": {
    "com.sicy.psd2ugui": "https://github.com/<你的账号>/Psd2UGUI.git"
  }
}
```

**方式二：本地包**

把仓库放到任意位置（**不要**放进 `Assets/` 下面），然后：

```json
{ "dependencies": { "com.sicy.psd2ugui": "file:../../Psd2UGUI" } }
```

**方式三：直接拷进 Assets**

把 `Editor/`、`Runtime/`、`package.json` 拷到 `Assets/PSD2UGUI/` 下。能用，但升级时容易漏文件。

> 装完后如果什么都跑不起来，先确认 Unity 控制台没有编译错误——`Editor/Tmp/` 是独立程序集，
> 没装 TMP 时它整体不参与编译，属正常现象，不是报错。

## 3. 快速开始

1. 把 `.psd` 拖进 Unity 工程（放在 `Assets/` 下任意位置都可以；也可以直接给绝对路径）。
2. 在 Project 窗口里选中这个 PSD。
3. 右键 → `PSD2UGUI/一键生成 Prefab`（快捷键 `Ctrl/Cmd + Shift + G`）。
4. 生成完后在 Project 里看 `Assets/PSD2UGUI/prefab/<模块>/`，Prefab 就在那儿。

想要更多控制（改控件类型、看诊断、改导出选项），走窗口：

`菜单栏 Window → PSD2UGUI → 导入窗口`，把 PSD 拖进去，右边有全部选项。

## 4. 菜单一览

| 菜单项 | 行为 |
| --- | --- |
| `Assets/PSD2UGUI/一键生成 Prefab` | 解析 → 导资源 → 生成/更新 Prefab → 写报告（最常用） |
| `Assets/PSD2UGUI/只导出资源` | 只导 PNG 与契约，不生成 Prefab |
| `Assets/PSD2UGUI/只解析并打印结构` | 什么都不写盘，只把节点树打到控制台，用来快速看解析结果 |
| `Assets/PSD2UGUI/生成所在文件夹里的所有 PSD` | 批量跑同目录下的全部 PSD |
| `Assets/PSD2UGUI/打开导入窗口` | 等价于 `Window/PSD2UGUI/导入窗口` |

前四项只在真的选中了 `.psd` 时才可用，其余情况自动置灰。

## 5. 产物落在哪里

```text
Assets/PSD2UGUI/
├─ sprite/<模块>/       PNG（文件名带内容哈希：名字_宽x高_哈希.png）
├─ prefab/<模块>/       .prefab
├─ contract/<模块>/     .contract.json    契约快照，纯文本、顺序固定，可以提交进仓库
├─ manifest/<模块>/     .psd2ugui.json    导出清单，重新导出时判断增量与失效文件
├─ report/<模块>/       .report.json      体检报告
└─ overrides/<模块>/    .overrides.json   人工覆盖表（只有你手动写入过才存在）
```

模块名默认取 `AssetRoot` 定义里没写时的 `common`，也可以在窗口或参数里指定。
**同模块之间的资源可以互相复用**（`ref` 引用），不同模块互相隔离。

`AssetRoot` 可以改（默认 `Assets/PSD2UGUI`），但必须落在 `Assets/` 下，
否则 Unity 的资源 API 不认，会直接报 `resource.outside-assets` 错误。

## 6. 选项

窗口右侧按组排布，对应的就是 `Psd2UguiRunOptions` 的字段。

| 组 | 选项 | 默认 | 说明 |
| --- | --- | --- | --- |
| 语义 | 保留隐藏图层 | 开 | 隐藏图层仍然进节点树，只是 `SetActive(false)`，方便后面在编辑器里打开 |
| 导出 | 自动九宫检测 | 开 | 自动找可拉伸的边框；也可以用 `sliced` 标签强制 |
| 导出 | 跨界面复用共享贴图 | 开 | 本文件里没有的 `ref` 去其它界面已导出的图里找 |
| 装配 | 根节点带 Canvas | 开 | 根节点挂 Canvas + CanvasScaler + GraphicRaycaster，拖进场景就能看 |
| 装配 | 标出节点来源图层 | 开 | 每个节点挂 `Psd2UguiNode`，记录来自哪个图层。**关掉会让增量更新失效** |
| 装配 | 文本效果（描边/投影） | 开 | 用 TMP 的能力近似 PSD 的描边、投影、发光 |
| 装配 | 已有预制体时增量更新 | 开 | 关掉则每次整棵重建，人工改动会被冲掉 |
| 产出 | 导出贴图 / 生成预制体 / 跑预检 / 写体检报告 | 全开 | 只解析时全部关掉即可 |

其它不在窗口里、但可以用代码/批处理设的：`PixelsPerUnit`（默认 100）、
`MaxTextureSize`（默认 4096）、`UncompressedTextures`（默认开，UI 图不压缩可避免色带与边缘脏点）。

## 7. 图层怎么命名

一句话：**名字后面跟点号加标签**，例如 `Arrow.img.dpdicon`。

- 控件类型：`img`、`btn`、`tmptxt`、`sld`、`sv`…
- 子节点角色：`bg`、`fill`、`handle`、`label`、`template`…
- 开关：`sliced`（按九宫导出）、`ignore`（整棵子树不导）、`ref 名字`（复用已有图）、`refp 名字`（复用已有 Prefab）

完整清单见 **[图层命名与覆盖表](TAGS.md)**。不写标签也能跑：解析器会先看文本/分组/纯色这类
结构性事实，再看名字关键词猜，每条推断都会带上 `type-source: inferred` 标记，窗口里能直接看到。

## 8. 推断错了怎么办

三种改法，按「影响范围从小到大」：

1. **改 PSD 图层名**：加个明确标签（`ButtonBlack.btn`），重新导出即可。适合批量、长期维护。
2. **用窗口改**：选中节点 → 改类型/角色 → `写入覆盖表`。适合少量临时修正，
   改完会落到 `overrides/<模块>/<源>.overrides.json`，跟着工程一起提交，别人拉下来也生效。
3. **直接手改覆盖表 JSON**：适合批量脚本化修改，格式见 [TAGS.md](TAGS.md#5-覆盖表)。

覆盖表比标签优先，标签比推断优先。匹配不上的条目会报 `override.unmatched` 警告，
不会静默失效。

## 9. 增量更新：怎么保证手改不被冲

生成出来的每个节点都挂 `Psd2UguiNode`，记着它来自哪个图层。重新导出时：

- 认得出来的节点 → 只覆盖**工具管的属性**（位置、尺寸、图片、文本、颜色、九宫）；
- 认不出来的东西（你手加的节点、手加的组件）→ 一律当人工内容，保留；
- 上一次是工具生成、这次设计稿里已经删掉的 → 才删。

手动改法的推荐姿势：**改 `Psd2UguiNode` 之外的东西**（换 Sprite、加子节点、加组件、改颜色）。
直接改 RectTransform 的位置尺寸会在下次导出时被设计稿覆盖——那是它的本职。

真要保住位置，用覆盖表或者干脆把节点从工具的管理范围里摘出去（删掉 `Psd2UguiNode`）。

## 10. 命令行 / CI

```bash
Unity -batchmode -quit -projectPath <工程路径> \
  -executeMethod Psd2Ugui.Editor.Batch.Psd2UguiBatch.Run \
  -psd2ugui-module common \
  -psd2ugui-psdDir Assets/UI \
  -logFile /tmp/psd2ugui.log
```

| 参数 | 说明 |
| --- | --- |
| `-psd2ugui-psd <路径>` | 指定一个 PSD，可重复出现多次 |
| `-psd2ugui-psdDir <目录>` | 指定一个目录，递归收集里面的 `.psd` |
| `-psd2ugui-module <名字>` | 产物落到哪个模块目录 |
| `-psd2ugui-no-prefab` | 只导资源，不生成 Prefab |

**退出码**：`0` 全部成功；`1` 有文件失败；`2` 一个 PSD 都没指定。

## 11. 用代码调

```csharp
using Psd2Ugui.Editor;
using Psd2Ugui.Core.Pipeline;

// 最省事：
Psd2UguiRunResult result = Psd2UguiPipeline.Run("Assets/UI/Login.psd", "login");
Debug.Log(result.BuildSummary());

// 要细粒度控制：
var options = new Psd2UguiRunOptions { PsdPath = "Assets/UI/Login.psd" };
options.Export.Module = "login";
options.Export.AssetRoot = "Assets/Art/UI";
options.Semantics.TextType = Psd2Ugui.Core.Contract.UiElementType.Text;  // 全部用 UGUI Text
options.Build.RootCanvas = false;
options.WriteReport = true;

Psd2UguiRunResult run = Psd2UguiPipeline.Run(options);
if (!run.Success)
{
    foreach (var d in run.Document.Diagnostics)
    {
        Debug.LogWarning(d.ToString());
    }
}
```

`Psd2UguiRunResult` 里有：`Document`（契约）、`ExportPlan`（资源计划）、`Sprites`（落盘结果）、
`Prefab`（装配结果）、`Report`（体检报告），以及 `ContractPath` / `ReportPath` / `PrefabPath` 三个产物路径。

纯解析（不写任何文件）：

```csharp
UiDocument document = Psd2UguiPipeline.Parse("Assets/UI/Login.psd");
```

## 12. 脱离 Unity 排查

命令行工具可以直接看解析结果，不用开编辑器：

```bash
# 打印图层树与节点语义
dotnet run --project "Tools~/PsdDump" -- path/to/Login.psd --nodes

# 导出契约 JSON，方便 diff
dotnet run --project "Tools~/PsdDump" -- path/to/Login.psd --json /tmp/Login.contract.json

# 跑预检并写体检报告
dotnet run --project "Tools~/PsdDump" -- path/to/Login.psd --preflight --report /tmp/Login.report.json
```

`Tools~/` 以 `~` 结尾，Unity 不会编译它。完整参数列表见 `Tools~/PsdDump/Program.cs` 开头的用法说明。

## 13. 常见问题

**Q：生成的图全是空白 / 是纯色块。**
先看控制台有没有 `resource.empty`。这个诊断表示该图层在 PSD 里没有可导出的像素
（常见于纯矢量形状、只有图层样式的图层、或者图层被完全裁掉了）。
真正的解决办法是在 PS 里把该图层栅格化，或者把它改成用别的图层承载。

**Q：字体不对 / 文本间距怪。**
PSD 存的是字体名，工程里没有同名字体时会报 `preflight.font-missing`。
把字体资产放进工程并保证**名字与 PSD 里一致**即可。字号是按像素写入的，
文本块的宽度由 PSD 的文本框决定，行距/字距按比例映射，不保证与 PS 里逐像素一致。

**Q：九宫边框不对。**
先看 `report.json` 里这张图的 `border`。检测不出来时用 `sliced` 标签强制开，
或者在覆盖表里写 `{"nineSlice": true}` 手填边框（自动检测只在能明确找到边框时才会给值）。

**Q：重新导出后我的改动没了。**
见第 9 节。另外确认「标出节点来源图层」是开着的——关掉它，工具就认不出哪些是自己生成的，
行为会退化（要么全留要么全删）。

**Q：想改控件生成的默认样子（比如按钮的默认颜色）。**
改 `Editor/Build/ControlFactory.cs`，那里是控件装配的唯一入口。

**Q：PSD 里有智能对象 / 16bit / CMYK。**
见 [已知限制](LIMITATIONS.md)。

**Q：`AssetRoot` 能改到 `Assets` 外面吗？**
不能。产物必须是 Unity 资产，否则 `AssetDatabase` 全都不认。
