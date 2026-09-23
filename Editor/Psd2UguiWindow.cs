using System.Collections.Generic;
using System.IO;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Semantics;
using Psd2Ugui.Editor.Batch;
using UnityEditor;
using UnityEngine;

namespace Psd2Ugui.Editor
{
    /// <summary>
    /// 导入窗口：拖一个 PSD 进来 → 看一眼解析出来的节点树 → 改不对的类型/角色 → 生成预制体。
    ///
    /// 窗口只负责「收集参数 + 展示结果 + 写覆盖表」，真正的流程全在 <see cref="Psd2UguiPipeline"/> 里，
    /// 所以右键菜单与 CI 批处理跑出来的结果和这里完全一致。
    /// </summary>
    public sealed class Psd2UguiWindow : EditorWindow
    {
        private const float TreeWidth = 320f;

        private Object _psdAsset;
        private string _assetRoot = "Assets/PSD2UGUI";
        private string _module = string.Empty;
        private int _pixelsPerUnit = 100;
        private bool _detectNineSlice = true;
        private bool _includeHidden = true;
        private bool _rootCanvas = true;
        private bool _markNodes = true;
        private bool _applyEffects = true;
        private bool _exportSprites = true;
        private bool _generatePrefab = true;
        private bool _incremental = true;
        private bool _runPreflight = true;
        private bool _writeReport = true;
        private bool _reuseShared = true;
        private bool _showOptions = true;

        private Psd2UguiRunResult _result;
        private UiNode _selected;
        private UiElementType _editType = UiElementType.None;
        private UiRole _editRole = UiRole.None;
        private Vector2 _treeScroll;
        private Vector2 _detailScroll;
        private Vector2 _mainScroll;
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private string _status = string.Empty;

        [MenuItem("Window/PSD2UGUI/导入窗口")]
        public static void ShowWindow()
        {
            ShowWindow(null);
        }

        /// <summary>带一个 PSD 打开窗口（右键菜单调用）。</summary>
        public static void ShowWindow(string psdPath)
        {
            var window = GetWindow<Psd2UguiWindow>("PSD2UGUI");
            window.minSize = new Vector2(720f, 460f);
            if (!string.IsNullOrEmpty(psdPath))
            {
                window.SetPsd(psdPath);
            }

            window.Show();
        }

        private void SetPsd(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset != null)
            {
                _psdAsset = asset;
            }

            Repaint();
        }

        private void OnGUI()
        {
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);

            EditorGUILayout.Space();
            DrawSource();
            EditorGUILayout.Space();
            DrawOptions();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("一键生成 Prefab", GUILayout.Height(28f)))
                {
                    Run(true, true);
                }

                if (GUILayout.Button("只解析", GUILayout.Height(28f)))
                {
                    Run(false, false);
                }

                if (GUILayout.Button("只导出资源", GUILayout.Height(28f)))
                {
                    Run(true, false);
                }
            }

            EditorGUILayout.Space();
            DrawStatus();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTree();
                DrawDetails();
            }

            EditorGUILayout.Space();
            DrawDiagnostics();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSource()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _psdAsset = EditorGUILayout.ObjectField("源 PSD", _psdAsset, typeof(Object), false);
                if (GUILayout.Button("用当前选中", GUILayout.Width(90f)))
                {
                    string selected = Psd2UguiMenu.SelectedPsd();
                    if (selected == null)
                    {
                        _status = "Project 里没有选中 PSD。";
                    }
                    else
                    {
                        SetPsd(selected);
                    }
                }
            }

            string path = PsdPath();
            if (string.IsNullOrEmpty(path))
            {
                EditorGUILayout.HelpBox("把 .psd / .psb 拖到上面的格子里（或在 Project 里选中后点「用当前选中」）。",
                    MessageType.Info);
            }
            else if (!File.Exists(path))
            {
                EditorGUILayout.HelpBox("文件不存在：" + path, MessageType.Error);
            }
        }

        private void DrawOptions()
        {
            _showOptions = EditorGUILayout.Foldout(_showOptions, "生成选项", true);
            if (!_showOptions)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                _assetRoot = EditorGUILayout.TextField("产物根目录", _assetRoot);
                _module = EditorGUILayout.TextField("模块名（留空 = 文档默认）", _module);
                _pixelsPerUnit = Mathf.Max(1, EditorGUILayout.IntField("Pixels Per Unit", _pixelsPerUnit));

                EditorGUILayout.LabelField("语义", EditorStyles.boldLabel);
                _includeHidden = EditorGUILayout.Toggle("保留隐藏图层", _includeHidden);

                EditorGUILayout.LabelField("导出", EditorStyles.boldLabel);
                _detectNineSlice = EditorGUILayout.Toggle("自动九宫检测", _detectNineSlice);
                _reuseShared = EditorGUILayout.Toggle("跨界面复用共享贴图", _reuseShared);

                EditorGUILayout.LabelField("装配", EditorStyles.boldLabel);
                _rootCanvas = EditorGUILayout.Toggle("根节点带 Canvas", _rootCanvas);
                _markNodes = EditorGUILayout.Toggle("标出节点来源图层", _markNodes);
                _applyEffects = EditorGUILayout.Toggle("文本效果（描边/投影）", _applyEffects);
                _incremental = EditorGUILayout.Toggle("已有预制体时增量更新", _incremental);

                EditorGUILayout.LabelField("产出", EditorStyles.boldLabel);
                _exportSprites = EditorGUILayout.Toggle("导出贴图", _exportSprites);
                _generatePrefab = EditorGUILayout.Toggle("生成预制体", _generatePrefab);
                _runPreflight = EditorGUILayout.Toggle("跑预检", _runPreflight);
                _writeReport = EditorGUILayout.Toggle("写体检报告", _writeReport);
            }
        }

        private void DrawStatus()
        {
            if (_result == null)
            {
                if (!string.IsNullOrEmpty(_status))
                {
                    EditorGUILayout.HelpBox(_status, MessageType.None);
                }

                return;
            }

            MessageType type = _result.Errors > 0
                ? MessageType.Error
                : (_result.Warnings > 0 ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.HelpBox(_result.BuildSummary(), type);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_result.PrefabPath)))
                {
                    if (GUILayout.Button("选中预制体"))
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_result.PrefabPath);
                        if (prefab != null)
                        {
                            Selection.activeObject = prefab;
                            EditorGUIUtility.PingObject(prefab);
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_result.ReportPath)))
                {
                    if (GUILayout.Button("打开报告"))
                    {
                        EditorUtility.RevealInFinder(_result.ReportPath);
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_result.PrefabPath)))
                {
                    if (GUILayout.Button("打开产物目录"))
                    {
                        EditorUtility.RevealInFinder(Path.GetDirectoryName(_result.PrefabPath));
                    }
                }

                if (GUILayout.Button("复制诊断"))
                {
                    GUIUtility.systemCopyBuffer = DiagnosticText();
                    _status = "诊断已复制到剪贴板。";
                }
            }
        }

        private void DrawTree()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(TreeWidth)))
            {
                EditorGUILayout.LabelField("节点树", EditorStyles.boldLabel);
                _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, GUILayout.Height(280f));
                UiDocument document = Document();
                if (document != null && document.Root != null)
                {
                    DrawNode(document.Root, 0);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawNode(UiNode node, int depth)
        {
            if (node == null)
            {
                return;
            }

            string key = NodeKey(node);
            bool hasChildren = node.Children.Count > 0;
            if (hasChildren && !_expanded.Contains(key))
            {
                _expanded.Add(key);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(depth * 12f);
                if (hasChildren)
                {
                    // 三角只负责展开/收起，名字按钮负责选中，两者互不干扰
                    bool expanded = _expanded.Contains(key);
                    bool next;
                    using (new EditorGUILayout.HorizontalScope(GUILayout.Width(14f)))
                    {
                        next = EditorGUILayout.Foldout(expanded, string.Empty, true);
                    }
                    if (next != expanded)
                    {
                        if (next)
                        {
                            _expanded.Add(key);
                        }
                        else
                        {
                            _expanded.Remove(key);
                        }
                    }
                }
                else
                {
                    GUILayout.Space(14f);
                }

                if (GUILayout.Button(Label(node), _selected == node ? EditorStyles.boldLabel : EditorStyles.label))
                {
                    Select(node);
                }
            }

            if (hasChildren && _expanded.Contains(key))
            {
                for (int i = 0; i < node.Children.Count; i++)
                {
                    DrawNode(node.Children[i], depth + 1);
                }
            }
        }

        private static string NodeKey(UiNode node)
        {
            return string.IsNullOrEmpty(node.Id)
                ? node.LayerPath + "#" + node.LayerId
                : node.Id;
        }

        private static string Label(UiNode node)
        {
            string extra = node.Type == UiElementType.None ? string.Empty : "  [" + node.Type.ToContract() + "]";
            if (node.Role != UiRole.None)
            {
                extra += "<" + node.Role.ToContract() + ">";
            }

            return node.Name + extra;
        }

        private void DrawDetails()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("节点与覆盖表", EditorStyles.boldLabel);
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(280f));

                if (_selected == null)
                {
                    EditorGUILayout.HelpBox("在左边的树里点一个节点，就能改它的类型或角色；" +
                                            "改动写进 overrides 文件，重新导出时优先于图层名标签。",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.LabelField(_selected.Name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("图层路径", _selected.LayerPath);
                    EditorGUILayout.LabelField("图层 ID", _selected.LayerId.ToString());
                    EditorGUILayout.LabelField("矩形", _selected.Rect.ToString());
                    EditorGUILayout.LabelField("类型来源", TypeSource(_selected));

                    _editType = (UiElementType)EditorGUILayout.EnumPopup("类型", _editType);
                    _editRole = (UiRole)EditorGUILayout.EnumPopup("角色", _editRole);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("写入覆盖表"))
                        {
                            ApplyOverride();
                        }

                        if (GUILayout.Button("移除这条覆盖"))
                        {
                            RemoveOverride();
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawDiagnostics()
        {
            UiDocument document = Document();
            if (document == null || document.Diagnostics.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("诊断（" + document.Diagnostics.Count + "）", EditorStyles.boldLabel);
            for (int i = 0; i < document.Diagnostics.Count; i++)
            {
                UiDiagnostic diagnostic = document.Diagnostics[i];
                MessageType type = diagnostic.Severity == DiagnosticSeverity.Error
                    ? MessageType.Error
                    : (diagnostic.Severity == DiagnosticSeverity.Warning ? MessageType.Warning : MessageType.None);
                EditorGUILayout.HelpBox(diagnostic.Code + ": " + diagnostic.Message, type);
            }
        }

        private void Select(UiNode node)
        {
            _selected = node;
            _editType = node.Type;
            _editRole = node.Role;
        }

        private static string TypeSource(UiNode node)
        {
            string source;
            if (node.Tags != null && node.Tags.TryGetValue("type-source", out source))
            {
                switch (source)
                {
                    case "tag":
                        return "图层名标签";
                    case "override":
                        return "覆盖表";
                    case "inferred":
                        return "按图层名猜的";
                }
            }

            return "无";
        }

        private UiDocument Document()
        {
            return _result == null ? null : _result.Document;
        }

        private string PsdPath()
        {
            return _psdAsset == null ? null : AssetDatabase.GetAssetPath(_psdAsset);
        }

        private void Run(bool exportSprites, bool generatePrefab)
        {
            string path = PsdPath();
            if (string.IsNullOrEmpty(path))
            {
                _status = "先选一个 PSD。";
                return;
            }

            Psd2UguiRunOptions options = BuildOptions(exportSprites, generatePrefab);
            Psd2UguiRunResult result = Psd2UguiPipeline.Run(options);
            _result = result;
            _status = result.Success ? "完成。" : "有错误级诊断，先看下面的列表。";
            _selected = null;
            Repaint();
        }

        private Psd2UguiRunOptions BuildOptions(bool exportSprites, bool generatePrefab)
        {
            // 「只解析」不该往磁盘上写东西：报告与契约跟着产物一起写
            bool writes = exportSprites || generatePrefab;
            var options = new Psd2UguiRunOptions
            {
                PsdPath = PsdPath(),
                ExportSprites = exportSprites,
                GeneratePrefab = generatePrefab,
                RunPreflight = _runPreflight,
                WriteReport = _writeReport && writes,
                WriteContract = writes,
                ReuseSharedResources = _reuseShared,
                IncrementalPrefab = _incremental
            };

            options.Export.AssetRoot = string.IsNullOrEmpty(_assetRoot) ? "Assets/PSD2UGUI" : _assetRoot.TrimEnd('/');
            options.Export.Module = string.IsNullOrEmpty(_module) ? null : _module;
            options.Export.DetectNineSlice = _detectNineSlice;
            options.Export.IncludeHiddenLayers = _includeHidden;
            options.Export.PixelsPerUnit = _pixelsPerUnit;

            options.Build.Module = options.Export.Module;
            options.Build.PixelsPerUnit = _pixelsPerUnit;
            options.Build.RootCanvas = _rootCanvas;
            options.Build.MarkNodes = _markNodes;
            options.Build.ApplyEffects = _applyEffects;

            options.Semantics.Module = string.IsNullOrEmpty(_module) ? "common" : _module;
            options.Semantics.KeepHiddenLayers = _includeHidden;
            return options;
        }

        /// <summary>把当前选中的节点写成一条覆盖（用图层 ID 做键，改名字也不会失效）。</summary>
        private void ApplyOverride()
        {
            if (_selected == null)
            {
                return;
            }

            Psd2UguiRunOptions options = BuildOptions(_exportSprites, _generatePrefab);
            string sourceName = Path.GetFileName(PsdPath());
            NodeOverrides overrides = Psd2UguiPipeline.LoadOverrides(options, sourceName) ?? new NodeOverrides();
            NodeOverride entry = overrides.Set("#" + _selected.LayerId);
            entry.HasType = _editType != UiElementType.None;
            entry.Type = _editType;
            entry.HasRole = _editRole != UiRole.None;
            entry.Role = _editRole;
            Psd2UguiPipeline.SaveOverrides(options.Export, sourceName, overrides);
            _status = "已写入覆盖表：" + _selected.Name + " → " + _editType.ToContract() + " / " + _editRole.ToContract();
            Reparse();
        }

        private void RemoveOverride()
        {
            if (_selected == null)
            {
                return;
            }

            Psd2UguiRunOptions options = BuildOptions(_exportSprites, _generatePrefab);
            string sourceName = Path.GetFileName(PsdPath());
            NodeOverrides overrides = Psd2UguiPipeline.LoadOverrides(options, sourceName);
            if (overrides == null || !overrides.Remove("#" + _selected.LayerId))
            {
                _status = "这个节点本来就没有覆盖。";
                return;
            }

            Psd2UguiPipeline.SaveOverrides(options.Export, sourceName, overrides);
            _status = "已移除覆盖：" + _selected.Name;
            Reparse();
        }

        /// <summary>改完覆盖立刻重解析，让用户马上看到效果（不落产品文件）。</summary>
        private void Reparse()
        {
            string path = PsdPath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Psd2UguiRunOptions options = BuildOptions(false, false);
            string sourceName = Path.GetFileName(path);
            Psd2UguiPipeline.LoadOverrides(options, sourceName);
            UiDocument document = Psd2UguiPipeline.Parse(path, options.Semantics);
            if (document == null)
            {
                return;
            }

            _result = _result ?? new Psd2UguiRunResult();
            _result.Document = document;
            _result.PsdPath = path;
            _selected = FindNode(document, _selected == null ? -1 : _selected.LayerId);
            if (_selected != null)
            {
                _editType = _selected.Type;
                _editRole = _selected.Role;
            }

            Repaint();
        }

        private static UiNode FindNode(UiDocument document, int layerId)
        {
            if (document == null || layerId < 0)
            {
                return null;
            }

            foreach (UiNode node in document.Nodes())
            {
                if (node.LayerId == layerId)
                {
                    return node;
                }
            }

            return null;
        }

        private string DiagnosticText()
        {
            UiDocument document = Document();
            if (document == null)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder();
            builder.Append("PSD2UGUI 诊断（").Append(document.Document.FileName).Append("）\n");
            for (int i = 0; i < document.Diagnostics.Count; i++)
            {
                builder.Append(document.Diagnostics[i]).Append('\n');
            }

            return builder.ToString();
        }
    }
}
