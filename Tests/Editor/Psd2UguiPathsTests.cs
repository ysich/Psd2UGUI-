using NUnit.Framework;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Editor.Import;

namespace Psd2Ugui.Tests
{
    /// <summary>产物目录约定：路径算错的话，编辑器里会「生成了但找不到」。</summary>
    public class Psd2UguiPathsTests
    {
        [Test]
        public void 各类产物都在同一个根目录下按模块分()
        {
            var options = new ExportOptions { AssetRoot = "Assets/PSD2UGUI/", Module = "login" };

            Assert.AreEqual("Assets/PSD2UGUI", Psd2UguiPaths.AssetRoot(options));
            Assert.AreEqual("Assets/PSD2UGUI/sprite/login", Psd2UguiPaths.SpriteDirectory(options));
            Assert.AreEqual("Assets/PSD2UGUI/prefab/login", Psd2UguiPaths.PrefabDirectory(options));
            Assert.AreEqual("Assets/PSD2UGUI/prefab/login/Login.prefab",
                Psd2UguiPaths.PrefabPath(options, "Login.psd"));
            Assert.AreEqual("Assets/PSD2UGUI/manifest/login/Login.psd2ugui.json",
                Psd2UguiPaths.ManifestPath(options, "Login.psd"));
            Assert.AreEqual("Assets/PSD2UGUI/report/login/Login.report.json",
                Psd2UguiPaths.ReportPath(options, "Login.psd"));
            Assert.AreEqual("Assets/PSD2UGUI/overrides/login/Login.overrides.json",
                Psd2UguiPaths.OverridePath(options, "Login.psd"));
        }

        [Test]
        public void 模块名留空时落进common()
        {
            var options = new ExportOptions { AssetRoot = "Assets/PSD2UGUI" };

            Assert.AreEqual("common", Psd2UguiPaths.ModuleName(options));
            Assert.AreEqual("Assets/PSD2UGUI/sprite/common", Psd2UguiPaths.SpriteDirectory(options));
        }

        [Test]
        public void 文件名里的非法字符会被收敛掉()
        {
            Assert.AreEqual("Login_主界面", Psd2UguiPaths.SafeName("Login:主界面.psd"));
            Assert.AreEqual("psd", Psd2UguiPaths.SafeName(string.Empty));
        }

        [Test]
        public void 产物必须落在Assets下()
        {
            Assert.IsTrue(Psd2UguiPaths.IsInsideAssets("Assets/PSD2UGUI"));
            Assert.IsTrue(Psd2UguiPaths.IsInsideAssets("Assets/PSD2UGUI/sprite"));
            Assert.IsFalse(Psd2UguiPaths.IsInsideAssets("Packages/com.foo"));
            Assert.IsFalse(Psd2UguiPaths.IsInsideAssets("/tmp/out"));
            Assert.IsFalse(Psd2UguiPaths.IsInsideAssets(null));
        }
    }
}
