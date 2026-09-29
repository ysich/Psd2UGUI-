using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Psd2Ugui.Core.Build;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Pipeline;
using Psd2Ugui.Core.Psd;
using Psd2Ugui.Core.Semantics;
using Psd2Ugui.Editor;
using Psd2Ugui.Testing;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Psd2Ugui.Tests
{
    /// <summary>
    /// 端到端：真的写 PSD 字节 → 真的导出贴图 → 真的存成预制体 → 真的重新导出一遍。
    ///
    /// 这些用例只能在 Unity 里跑（要 AssetDatabase 与 PrefabUtility），
    /// 但 PSD 夹具与 dotnet 测试用的是同一份源码（`Tests/Fixture/PsdFixtureBuilder.cs`），
    /// 所以「Core 侧规则」与「Unity 侧行为」不会各说各话。
    ///
    /// PSD 写在系统临时目录里：它是输入，不必进 Assets（也就不会触发 Unity 反复导入）。
    /// </summary>
    public class Psd2UguiPipelineTests
    {
        private const string OutputRoot = "Assets/PSD2UGUI-Test/Out";
        private const string TestFolder = "Assets/PSD2UGUI-Test";

        private string _tempDir;
        private string _psdPath;

        [SetUp]
        public void 准备环境()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }

            _tempDir = Path.Combine(Path.GetTempPath(), "psd2ugui-editmode");
            Directory.CreateDirectory(_tempDir);
            _psdPath = Path.Combine(_tempDir, "Fixture.psd");
        }

        [TearDown]
        public void 清理环境()
        {
            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }

            if (File.Exists(_psdPath))
            {
                File.Delete(_psdPath);
            }
        }

        [Test]
        public void 一键生成产出贴图契约报告与预制体()
        {
            WriteFixture("Bg.img", "Button.btn");

            Psd2UguiRunResult result = Run();

            Assert.IsTrue(result.Success, "不该有错误级诊断：" + Describe(result));
            Assert.AreEqual(2, result.ExportPlan.Sprites.Count, "两个图层各出一张图");
            Assert.IsTrue(File.Exists(result.ContractPath), "契约 JSON 没写出来");
            Assert.IsTrue(File.Exists(result.ReportPath), "体检报告没写出来");
            Assert.IsNotNull(result.Prefab);
            Assert.IsTrue(File.Exists(result.PrefabPath), "预制体没写出来");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            Assert.IsNotNull(prefab);

            Transform bg = prefab.transform.Find("m_img_Bg");
            Assert.IsNotNull(bg, "找不到 Bg 节点");
            Assert.IsNotNull(bg.GetComponent<Image>(), "Bg 应该是 Image");
            Assert.IsNotNull(bg.GetComponent<Psd2UguiNode>(), "节点上应该挂着来源标记");
            Assert.IsNotNull(bg.GetComponent<Image>().sprite, "Image 应该拿到导出的 Sprite");

            Transform button = prefab.transform.Find("m_btn_Button");
            Assert.IsNotNull(button, "找不到 Button 节点");
            Assert.IsNotNull(button.GetComponent<Button>(), "按标签生成的应该是 Button");
        }

        [Test]
        public void 裁过透明边的图层按内容框摆()
        {
            // 24x24 的图层矩形放在 (8,8)，只有中间 8x8 见方有像素
            WritePaddedFixture("Icon.img", 8, 8, 24, 8);

            Psd2UguiRunResult result = Run();

            Assert.IsTrue(result.Success, "不该有错误级诊断：" + Describe(result));
            Assert.AreEqual(1, result.ExportPlan.Sprites.Count, "只该出一张图");
            SpriteExport sprite = result.ExportPlan.Sprites[0];
            Assert.AreEqual(8, sprite.Bitmap.Width, "导出该把透明边裁掉");
            Assert.AreEqual(new UiRect(8d, 8d, 8d, 8d), sprite.SourceRect);

            GameObject contents = PrefabUtility.LoadPrefabContents(result.PrefabPath);
            try
            {
                Transform icon = contents.transform.Find("m_img_Icon");
                Assert.IsNotNull(icon, "找不到 Icon 节点");
                var rect = (RectTransform)icon;
                // 位置补上被裁掉的左边与上边，尺寸就是内容框的 8x8，不再被拉满整张图层矩形
                Assert.AreEqual(new Vector2(16f, -16f), rect.anchoredPosition);
                Assert.AreEqual(new Vector2(8f, 8f), rect.sizeDelta);
                Assert.AreEqual(1, icon.GetComponents<Image>().Length);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        [Test]
        public void 重新导出保留人工改动()
        {
            WriteFixture("Bg.img");
            Psd2UguiRunResult first = Run();

            // 模拟美术/程序在预制体上手动改：换个颜色，再挂一个自己的子节点
            GameObject contents = PrefabUtility.LoadPrefabContents(first.PrefabPath);
            Transform bg = contents.transform.Find("m_img_Bg");
            var image = bg.GetComponent<Image>();
            image.color = new Color(1f, 0f, 0f, 1f);
            var handmade = new GameObject("人工加的", typeof(RectTransform));
            handmade.transform.SetParent(bg, false);
            PrefabUtility.SaveAsPrefabAsset(contents, first.PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            // 设计稿一个字没改，重新导出
            Psd2UguiRunResult second = Run();

            Assert.IsNotNull(second.Prefab);
            contents = PrefabUtility.LoadPrefabContents(second.PrefabPath);
            Transform again = contents.transform.Find("m_img_Bg");
            Assert.IsNotNull(again.Find("人工加的"), "人工加的子节点被删了");
            Assert.AreEqual(Color.red, again.GetComponent<Image>().color, "人工改的颜色被覆盖了");
            PrefabUtility.UnloadPrefabContents(contents);
        }

        [Test]
        public void 设计稿删掉的图层会从预制体里移除()
        {
            WriteFixture("Bg.img", "Icon.img");
            Psd2UguiRunResult first = Run();
            Assert.IsTrue(HasChild(first.PrefabPath, "m_img_Icon"), "第一次导出应该有 Icon");

            WriteFixture("Bg.img");
            Psd2UguiRunResult second = Run();

            Assert.IsNotNull(second.Prefab);
            Assert.IsTrue(HasChild(second.PrefabPath, "m_img_Bg"), "Bg 不该消失");
            Assert.IsFalse(HasChild(second.PrefabPath, "m_img_Icon"), "Icon 应该被移除");
        }

        [Test]
        public void 重复导出不会把组件挂两遍()
        {
            WriteFixture("Bg.img", "Button.btn");
            Run();
            Psd2UguiRunResult second = Run();

            GameObject contents = PrefabUtility.LoadPrefabContents(second.PrefabPath);
            Transform bg = contents.transform.Find("m_img_Bg");
            Assert.AreEqual(1, bg.GetComponents<Image>().Length, "Image 挂了两遍");
            Assert.AreEqual(1, bg.GetComponents<Graphic>().Length, "视觉件挂了两遍");

            Transform button = contents.transform.Find("m_btn_Button");
            Assert.AreEqual(1, button.GetComponents<Button>().Length, "Button 挂了两遍");
            PrefabUtility.UnloadPrefabContents(contents);
        }

        [Test]
        public void 共享贴图跨界面复用不重复落盘()
        {
            WriteFixtureWith("First.psd", "Logo.img");
            Psd2UguiRunResult first = RunNamed("First.psd", "shared");
            int pngBefore = CountPngs("Assets/PSD2UGUI-Test/Out/sprite/shared");
            Assert.AreEqual(1, pngBefore, "第一个界面应该导出一张 Logo");

            WriteFixtureWith("Second.psd", "ref Logo.img");
            Psd2UguiRunResult second = RunNamed("Second.psd", "shared");

            Assert.IsTrue(second.Success, "不该有错误级诊断：" + Describe(second));
            Assert.AreEqual(0, second.ExportPlan.Sprites.Count, "复用的图不该重新落盘");
            Assert.AreEqual(1, second.ExportPlan.Reused.Count, "应该复用到一张共享图");
            Assert.AreEqual(pngBefore, CountPngs("Assets/PSD2UGUI-Test/Out/sprite/shared"), "目录里多出了文件");

            GameObject contents = PrefabUtility.LoadPrefabContents(second.PrefabPath);
            Transform logo = contents.transform.Find("m_img_Logo");
            Assert.IsNotNull(logo, "找不到引用节点");
            Sprite sprite = logo.GetComponent<Image>().sprite;
            Assert.IsNotNull(sprite, "引用节点没拿到共享贴图");
            StringAssert.Contains("sprite/shared/Logo", AssetDatabase.GetAssetPath(sprite));
            PrefabUtility.UnloadPrefabContents(contents);
        }

        [Test]
        public void 覆盖表写进文件后重新导出就生效()
        {
            WriteFixture("Bg.img");
            Psd2UguiRunResult first = Run();
            bool firstIsImage = OnPrefab(first.PrefabPath,
                contents => contents.transform.Find("m_img_Bg").GetComponent<Image>() != null);
            Assert.IsTrue(firstIsImage, "默认应该是 Image");

            UiNode node = first.Document.Root.Children[0];
            var export = new ExportOptions { AssetRoot = OutputRoot };
            var overrides = new NodeOverrides();
            NodeOverride entry = overrides.Set("#" + node.LayerId);
            entry.HasType = true;
            entry.Type = UiElementType.Button;
            Psd2UguiPipeline.SaveOverrides(export, "Fixture.psd", overrides);

            Psd2UguiRunResult second = Run();

            bool secondIsButton = OnPrefab(second.PrefabPath,
                contents => contents.transform.Find("m_btn_Bg").GetComponent<Button>() != null);
            Assert.IsTrue(secondIsButton, "覆盖表没生效");
            Assert.AreEqual("override", second.Document.Root.Children[0].Tags["type-source"]);
        }

        [Test]
        public void 预检诊断会写进报告()
        {
            WriteFixture("Mark.img", "Mark.img");

            Psd2UguiRunResult result = Run();

            Assert.IsTrue(File.Exists(result.ReportPath));
            Assert.IsTrue(result.Report.Diagnostics.Exists(item => item.Code == "preflight.duplicate-sprite-name"),
                "重名资源应该被预检抓到：" + Describe(result));
        }

        [Test]
        public void 批处理遇到坏路径返回失败个数()
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                int failures = Psd2Ugui.Editor.Batch.Psd2UguiBatch.RunAll(
                    new List<string> { Path.Combine(_tempDir, "不存在.psd") });

                Assert.AreEqual(1, failures);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        /// <summary>
        /// 真实 PSD 冒烟。样本不入库（体积 + 授权），所以用环境变量指过来：
        ///   PSD2UGUI_SMOKE_PSD=/path/to/xxx.psd Tools~/dev-project.sh
        /// 没配就跳过——默认的 12 个用例用的是自造字节流，任何时候都能跑。
        /// </summary>
        [Test]
        public void 真实PSD冒烟()
        {
            string psd = Environment.GetEnvironmentVariable("PSD2UGUI_SMOKE_PSD");
            if (string.IsNullOrEmpty(psd) || !File.Exists(psd))
            {
                Assert.Ignore("没配 PSD2UGUI_SMOKE_PSD，跳过真实 PSD 冒烟");
                return;
            }

            var options = new Psd2UguiRunOptions { PsdPath = psd };
            options.Export.AssetRoot = OutputRoot;
            Psd2UguiRunResult result = Psd2UguiPipeline.Run(options);

            Assert.IsTrue(result.Success, "真实 PSD 跑出了错误级诊断：" + Describe(result));
            Assert.AreEqual(0, result.Errors, "不该有 Error 级诊断");
            Assert.IsNotNull(result.Prefab, "没生成预制体");
            Assert.IsTrue(File.Exists(result.PrefabPath), "预制体没落盘");
            Assert.IsTrue(File.Exists(result.ContractPath), "契约 JSON 没落盘");
            Assert.IsTrue(File.Exists(result.ReportPath), "体检报告没落盘");
            Assert.Greater(result.Document.Root.CountDescendants(), 1, "节点树是空的");
            Assert.Greater(result.ExportPlan.Sprites.Count, 0, "一张图都没导出");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath));

            UnityEngine.Debug.Log("[PSD2UGUI] 真实 PSD 冒烟：" + Path.GetFileName(psd) + " → " +
                                  result.Document.Root.CountDescendants() + " 节点 / " +
                                  result.ExportPlan.Sprites.Count + " 张图 / E" + result.Errors +
                                  " W" + result.Warnings + " I" + result.Infos);
            UnityEngine.Debug.Log("[PSD2UGUI] 诊断分布：" + DiagnosticHistogram(result));
        }

        private Psd2UguiRunResult Run()
        {
            return RunNamed("Fixture.psd", null);
        }

        private Psd2UguiRunResult RunNamed(string fileName, string module)
        {
            var options = new Psd2UguiRunOptions { PsdPath = Path.Combine(_tempDir, fileName) };
            options.Export.AssetRoot = OutputRoot;
            if (!string.IsNullOrEmpty(module))
            {
                options.Export.Module = module;
            }

            return Psd2UguiPipeline.Run(options);
        }

        private void WriteFixture(params string[] layerNames)
        {
            WriteFixtureWith("Fixture.psd", layerNames);
        }

        private void WriteFixtureWith(string fileName, params string[] layerNames)
        {
            var builder = new PsdFixtureBuilder { Width = 64, Height = 64 };
            for (int i = 0; i < layerNames.Length; i++)
            {
                Pixel(builder, layerNames[i], 16 + i, i);
            }

            File.WriteAllBytes(Path.Combine(_tempDir, fileName), builder.Build());
        }

        private static void Pixel(PsdFixtureBuilder builder, string name, int size, int seed)
        {
            LayerSpec layer = builder.AddLayer(name);
            layer.Left = 0;
            layer.Top = 0;
            layer.Right = size;
            layer.Bottom = size;
            int count = size * size;
            var channel = new byte[count];
            var alpha = new byte[count];
            for (int i = 0; i < count; i++)
            {
                channel[i] = (byte)(20 + seed * 10 + (i % 200));
                alpha[i] = 255;
            }

            layer.WithChannel(0, PsdCompression.Raw, channel)
                .WithChannel(1, PsdCompression.Raw, channel)
                .WithChannel(2, PsdCompression.Raw, channel)
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, alpha);
        }

        /// <summary>图层矩形 size×size 放在 (x,y)，只有中间 (size-2*pad) 见方有像素。</summary>
        private void WritePaddedFixture(string layerName, int x, int y, int size, int pad)
        {
            var builder = new PsdFixtureBuilder { Width = 64, Height = 64 };
            LayerSpec layer = builder.AddLayer(layerName);
            layer.Left = x;
            layer.Top = y;
            layer.Right = x + size;
            layer.Bottom = y + size;
            int count = size * size;
            var channel = new byte[count];
            var alpha = new byte[count];
            for (int i = 0; i < count; i++)
            {
                int px = i % size;
                int py = i / size;
                bool inner = px >= pad && px < size - pad && py >= pad && py < size - pad;
                channel[i] = 200;
                alpha[i] = (byte)(inner ? 255 : 0);
            }

            layer.WithChannel(0, PsdCompression.Raw, channel)
                .WithChannel(1, PsdCompression.Raw, channel)
                .WithChannel(2, PsdCompression.Raw, channel)
                .WithChannel(PsdChannelId.Transparency, PsdCompression.Raw, alpha);
            File.WriteAllBytes(_psdPath, builder.Build());
        }

        /// <summary>
        /// 在预制体副本上做一件事。
        /// LoadPrefabContents 出来的对象一旦 Unload 就会被销毁，
        /// 把 Transform 带回测试方法里用只会拿到 MissingReferenceException —— 所以只允许在回调里用。
        /// </summary>
        private static T OnPrefab<T>(string prefabPath, Func<GameObject, T> action)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                return action(contents);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool HasChild(string prefabPath, string name)
        {
            return OnPrefab(prefabPath, contents => contents.transform.Find(name) != null);
        }

        private static int CountPngs(string folder)
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").Length : 0;
        }

        /// <summary>按「严重度 码」统计一遍，冒烟时一眼能看出多出来的是什么。</summary>
        private static string DiagnosticHistogram(Psd2UguiRunResult result)
        {
            if (result.Document == null)
            {
                return "（没有文档）";
            }

            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            for (int i = 0; i < result.Document.Diagnostics.Count; i++)
            {
                UiDiagnostic item = result.Document.Diagnostics[i];
                string key = item.Severity + " " + item.Code;
                int seen;
                if (counts.TryGetValue(key, out seen))
                {
                    counts[key] = seen + 1;
                }
                else
                {
                    counts[key] = 1;
                    order.Add(key);
                }
            }

            order.Sort(StringComparer.Ordinal);
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("；");
                }

                builder.Append(order[i]).Append(" x").Append(counts[order[i]]);
            }

            return builder.Length == 0 ? "（没有诊断）" : builder.ToString();
        }

        private static string Describe(Psd2UguiRunResult result)
        {
            if (result.Document == null)
            {
                return "没有文档";
            }

            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < result.Document.Diagnostics.Count; i++)
            {
                builder.Append(result.Document.Diagnostics[i]).Append("; ");
            }

            return builder.ToString();
        }
    }
}
