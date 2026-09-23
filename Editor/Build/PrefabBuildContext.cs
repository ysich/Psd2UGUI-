using System.Collections.Generic;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Editor.Import;
using UnityEditor;
using UnityEngine;
// UnityEditor 里也有个 BuildOptions（构建目标），这里明确指我们自己的
using BuildOptions = Psd2Ugui.Core.Build.BuildOptions;

namespace Psd2Ugui.Editor.Build
{
    /// <summary>装配过程中的共享状态：贴图/字体缓存、诊断收集。</summary>
    public sealed class PrefabBuildContext
    {
        public UiDocument Document;
        public BuildOptions Build = new BuildOptions();
        public ExportOptions Export = new ExportOptions();
        public string SourcePsd = string.Empty;

        public int NodesCreated;
        public int NodesReused;

        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly HashSet<string> _reported = new HashSet<string>();
        private bool _defaultFontResolved;
        private Font _defaultFont;
        private Texture2D _white;

        /// <summary>按资源 ID 取已经导出到工程里的 Sprite。</summary>
        public Sprite Sprite(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                return null;
            }

            Sprite cached;
            if (_sprites.TryGetValue(resourceId, out cached))
            {
                return cached;
            }

            UiResource resource = Document.FindResource(resourceId);
            var sprite = resource == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Sprite>(SpriteAssetPath(resource));
            _sprites[resourceId] = sprite;
            if (sprite == null)
            {
                Report(DiagnosticSeverity.Warning, "prefab.sprite-missing",
                    "找不到贴图资源，节点会显示成白块：" + (resource == null ? resourceId : resource.FileName));
            }

            return sprite;
        }

        public string SpriteAssetPath(UiResource resource)
        {
            return Psd2UguiPaths.AssetRoot(Export) + "/" + resource.ContractPath;
        }

        /// <summary>一张 1x1 白图，给没贴图但有颜色的 Image 兜底。</summary>
        public Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = Texture2D.whiteTexture;
                }

                return _white;
            }
        }

        /// <summary>按 PSD 里的字体名找工程里的字体；找不到就用默认字体。</summary>
        public Font Font(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return DefaultFont();
            }

            Font cached;
            if (_fonts.TryGetValue(name, out cached))
            {
                return cached == null ? DefaultFont() : cached;
            }

            Font found = FindFontAsset(name);
            _fonts[name] = found;
            if (found == null)
            {
                Report(DiagnosticSeverity.Info, "font.missing",
                    "工程里没有字体「" + name + "」，已改用默认字体");
            }

            return found ?? DefaultFont();
        }

        private Font DefaultFont()
        {
            if (_defaultFontResolved)
            {
                return _defaultFont;
            }

            _defaultFontResolved = true;
            try
            {
                _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch
            {
                _defaultFont = null;
            }

            return _defaultFont;
        }

        private static Font FindFontAsset(string name)
        {
            string[] guids = AssetDatabase.FindAssets("\"" + name + "\" t:Font");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (font != null && string.Equals(font.name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }
            }

            return null;
        }

        /// <summary>同一条诊断只报一次，避免大图刷屏。</summary>
        public void Report(DiagnosticSeverity severity, string code, string message)
        {
            if (!_reported.Add(code + "|" + message))
            {
                return;
            }

            Document.Report(severity, code, message);
        }
    }
}
