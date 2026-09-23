using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Imaging;
using Psd2Ugui.Core.Json;
using Psd2Ugui.Core.Semantics;


using Psd2Ugui.Core.Psd;

namespace Psd2Ugui.Tools
{
    /// <summary>
    /// 命令行 PSD 结构查看器：用于在没有 Unity 的环境下排查解析结果。
    /// 用法：dotnet run --project Tools~/PsdDump -- &lt;file.psd&gt; [--json out.json] [--layers out.json]

    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length == 0)
            {
                Console.WriteLine("用法: PsdDump <file.psd> [--json out.json] [--layers out.json] [--nodes]");
            Console.WriteLine("      [--pixels dir] [--composite file] [--overrides overrides.json] [--sprites dir]");
                return 2;
            }

            string path = args[0];
            string jsonPath = null;
            string layersPath = null;
            string pixelsPath = null;
            string compositePath = null;
            string overridesPath = null;
            string spritesPath = null;
            bool showNodes = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--json")
                {
                    jsonPath = Next(args, ref i);
                }
                else if (args[i] == "--layers")
                {
                    layersPath = Next(args, ref i);
                }
                else if (args[i] == "--pixels")
                {
                    pixelsPath = Next(args, ref i);
                }
                else if (args[i] == "--composite")
                {
                    compositePath = Next(args, ref i);
                }
                else if (args[i] == "--overrides")
                {
                    overridesPath = Next(args, ref i);
                }
                else if (args[i] == "--sprites")
                {
                    spritesPath = Next(args, ref i);
                }
                else if (args[i] == "--nodes")
                {
                    showNodes = true;
                }
            }



            if (!File.Exists(path))
            {
                Console.Error.WriteLine("文件不存在: " + path);
                return 2;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            PsdFile file = PsdParser.Read(File.ReadAllBytes(path), Path.GetFileName(path));
            stopwatch.Stop();

            Console.WriteLine("文件      : " + file.FileName);
            Console.WriteLine("版本      : " + (file.IsPsb ? "PSB" : "PSD") + " / " + file.BitDepth + "bit / 模式 " +
                              file.ColorMode + " / " + file.ChannelCount + " 通道");
            Console.WriteLine("画布      : " + file.Width + " x " + file.Height +
                              (file.ResolutionPpi > 0 ? " @ " + file.ResolutionPpi.ToString("0.##") + "ppi" : string.Empty));
            Console.WriteLine("图像资源  : " + file.ImageResources.Count + " 个");
            Console.WriteLine("图层      : " + file.Layers.Count + " 条记录，顶层 " + file.RootLayers.Count);
            Console.WriteLine("解析耗时  : " + stopwatch.ElapsedMilliseconds + " ms");

            if (file.Warnings.Count > 0)
            {
                Console.WriteLine("警告      : " + file.Warnings.Count);
                for (int i = 0; i < Math.Min(file.Warnings.Count, 20); i++)
                {
                    Console.WriteLine("  - " + file.Warnings[i]);
                }
            }

            Console.WriteLine();
            Console.WriteLine("图层树:");
            for (int i = file.RootLayers.Count - 1; i >= 0; i--)
            {
                DumpLayer(file.RootLayers[i], 0, file);
            }

            var buildOptions = new NodeBuildOptions();
            if (!string.IsNullOrEmpty(overridesPath))
            {
                buildOptions.Overrides = NodeOverrides.Parse(File.ReadAllText(overridesPath));
                Console.WriteLine("覆盖表    : " + overridesPath + " (" + buildOptions.Overrides.Entries.Count + " 条)");
            }

            UiDocument document = NodeBuilder.Build(file, buildOptions);
            PrintSemantics(document, showNodes);

            if (!string.IsNullOrEmpty(jsonPath))
            {
                File.WriteAllText(jsonPath, ContractJson.ToJsonText(document), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("已输出契约: " + jsonPath);
            }

            if (!string.IsNullOrEmpty(layersPath))
            {
                File.WriteAllText(layersPath, BuildLayerJson(file).ToJsonString(true), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("已输出图层明细: " + layersPath);
            }

            if (!string.IsNullOrEmpty(pixelsPath))
            {
                Console.WriteLine();
                Console.WriteLine("已输出图层像素: " + DumpPixels(file, pixelsPath) + " 个 -> " + pixelsPath);
            }

            if (!string.IsNullOrEmpty(spritesPath))
            {
                Console.WriteLine();
                Console.WriteLine("已输出 Sprite: " + DumpSprites(file, document, spritesPath) + " 个 -> " + spritesPath);
            }

            if (!string.IsNullOrEmpty(compositePath))
            {
                Bitmap canvas = LayerRasterizer.Composite(file, null, file.Warnings);
                if (canvas == null)
                {
                    Console.Error.WriteLine("合成失败：画布尺寸非法");
                    return 3;
                }

                File.WriteAllBytes(compositePath, canvas.Pixels);
                Console.WriteLine("已输出合成图: " + compositePath + " (" + canvas.Width + "x" + canvas.Height + ")");
            }

            return 0;
        }

        /// <summary>
        /// 把每个图层栅格化成裸 RGBA 字节（每像素 4 字节，行优先），
        /// 文件名用图层 ID，配合 Tools~/psd-tools-verify/dump_pixels.py 做逐像素对照。
        /// </summary>
        private static int DumpPixels(PsdFile file, string directory)
        {
            Directory.CreateDirectory(directory);
            var warnings = new List<string>();
            JsonValue layers = JsonValue.Array();
            int written = 0;
            foreach (PsdLayer layer in file.AllLayers())
            {
                if (layer.IsGroup || layer.IsBoundingDivider)
                {
                    continue;
                }

                Bitmap bitmap = LayerRasterizer.Rasterize(file, layer, warnings);
                if (bitmap == null || bitmap.IsEmpty)
                {
                    continue;
                }

                File.WriteAllBytes(Path.Combine(directory, layer.LayerId + ".rgba"), bitmap.Pixels);
                layers.Add(JsonValue.Object()
                    .Set("id", JsonValue.Number(layer.LayerId))
                    .Set("name", JsonValue.String(layer.DisplayName))
                    .Set("x", JsonValue.Number(layer.Rect.X))
                    .Set("y", JsonValue.Number(layer.Rect.Y))
                    .Set("width", JsonValue.Number(bitmap.Width))
                    .Set("height", JsonValue.Number(bitmap.Height))
                    .Set("opacity", JsonValue.Number(layer.Opacity))
                    .Set("visible", JsonValue.Bool(layer.Visible)));
                written++;
            }

            JsonValue index = JsonValue.Object()
                .Set("document", JsonValue.Object()
                    .Set("width", JsonValue.Number(file.Width))
                    .Set("height", JsonValue.Number(file.Height)))
                .Set("layers", layers);
            File.WriteAllText(Path.Combine(directory, "index.json"), index.ToJsonString(true), new UTF8Encoding(false));
            return written;

        }


        /// <summary>
        /// 输出图层明细，字段与 Tools~/psd-tools-verify/dump_reference.py 一一对应，
        /// 方便用 psd-tools 做交叉校验。
        /// </summary>
        private static JsonValue BuildLayerJson(PsdFile file)
        {
            JsonValue array = JsonValue.Array();
            foreach (PsdLayer layer in file.AllLayers())
            {
                if (layer.IsBoundingDivider)
                {
                    continue;
                }

                JsonValue rect = JsonValue.Array()
                    .Add(JsonValue.Number(layer.Rect.Y))
                    .Add(JsonValue.Number(layer.Rect.X))
                    .Add(JsonValue.Number(layer.Rect.Y + layer.Rect.Height))
                    .Add(JsonValue.Number(layer.Rect.X + layer.Rect.Width));

                JsonValue effects = JsonValue.Array();
                for (int i = 0; i < layer.Effects.Count; i++)
                {
                    effects.Add(JsonValue.String(layer.Effects[i].Kind));
                }

                JsonValue item = JsonValue.Object()
                    .Set("name", JsonValue.String(layer.DisplayName))
                    .Set("layer_id", JsonValue.Number(layer.LayerId))
                    .Set("rect", rect)
                    .Set("opacity", JsonValue.Number(layer.Opacity))
                    .Set("visible", JsonValue.Bool(layer.Visible))
                    .Set("clip", JsonValue.Number(layer.Clipping ? 1 : 0))
                    .Set("lsct", layer.DividerType < 0 ? JsonValue.Null : JsonValue.Number(layer.DividerType))
                    .Set("effects", effects)
                    .Set("fill", layer.HasSolidFill
                        ? JsonValue.String(layer.SolidFill.ToHexRgba())
                        : JsonValue.Null);

                if (layer.Text != null && layer.Text.HasValue)
                {
                    item.Set("text", JsonValue.String(layer.Text.Text))
                        .Set("font", JsonValue.String(layer.Text.FontName))
                        .Set("size", JsonValue.Number(layer.Text.FontSize))
                        .Set("align", JsonValue.String(layer.Text.Justification))
                        .Set("color", layer.Text.HasColor
                            ? JsonValue.Array()
                                .Add(JsonValue.Number(layer.Text.Color.A))
                                .Add(JsonValue.Number(layer.Text.Color.R))
                                .Add(JsonValue.Number(layer.Text.Color.G))
                                .Add(JsonValue.Number(layer.Text.Color.B))
                            : JsonValue.Null);
                }
                else
                {
                    item.Set("text", JsonValue.Null)
                        .Set("font", JsonValue.Null)
                        .Set("size", JsonValue.Null)
                        .Set("align", JsonValue.Null)
                        .Set("color", JsonValue.Null);
                }

                array.Add(item);
            }

            return array;
        }


        private static void DumpLayer(PsdLayer layer, int depth, PsdFile file)
        {
            var builder = new StringBuilder();
            builder.Append(new string(' ', depth * 2));
            builder.Append(layer.IsGroup ? "[组] " : layer.IsFillLayer ? "[填充] " : "[图层] ");
            builder.Append(layer.DisplayName);
            builder.Append("  rect=").Append(layer.Rect);
            if (!layer.Visible)
            {
                builder.Append("  (隐藏)");
            }

            if (layer.Opacity < 255)
            {
                builder.Append("  opacity=").Append(layer.Opacity);
            }

            if (layer.Clipping)
            {
                builder.Append("  (裁剪层)");
            }

            if (layer.Text != null && layer.Text.HasValue)
            {
                builder.Append("  text=\"").Append(layer.Text.Text.Replace("\n", "\\n")).Append('"');
                builder.Append(" font=").Append(layer.Text.FontName);
                builder.Append(" size=").Append(layer.Text.FontSize.ToString("0.##"));
                builder.Append(" color=").Append(layer.Text.Color);
                builder.Append(" align=").Append(layer.Text.Justification);
            }

            for (int i = 0; i < layer.Effects.Count; i++)
            {
                builder.Append("  fx:").Append(layer.Effects[i].Kind);
            }

            if (layer.HasSolidFill)
            {
                builder.Append("  fill=").Append(layer.SolidFill);
            }

            Console.WriteLine(builder.ToString());

            var ids = new List<string>();
            for (int i = 0; i < layer.Channels.Length; i++)
            {
                ids.Add(layer.Channels[i].Id.ToString());
            }

            if (depth == 0)
            {
                Console.WriteLine(new string(' ', depth * 2 + 2) + "channels=[" + string.Join(",", ids) + "] layerId=" +
                                  layer.LayerId);
            }

            for (int i = layer.Children.Count - 1; i >= 0; i--)
            {
                DumpLayer(layer.Children[i], depth + 1, file);
            }
        }

        /// <summary>
        /// 把 Image / RawImage 节点导成 PNG，并附上九宫检测结果。
        /// 图集之外的处理（导入设置、资源复用）在 Unity 侧的 Step 6 完成，这里只负责像素与九宫。
        /// </summary>
        private static int DumpSprites(PsdFile file, UiDocument document, string directory)
        {
            Directory.CreateDirectory(directory);
            var warnings = new List<string>();
            JsonValue index = JsonValue.Array();
            int written = 0;
            foreach (UiNode node in document.Nodes())
            {
                if (node.Type != UiElementType.Image && node.Type != UiElementType.RawImage)
                {
                    continue;
                }

                PsdLayer layer = file.FindLayer(node.LayerId);
                if (layer == null)
                {
                    continue;
                }

                Bitmap bitmap = LayerRasterizer.Rasterize(file, layer, warnings);
                if (bitmap == null || bitmap.IsEmpty || bitmap.IsFullyTransparent())
                {
                    continue;
                }

                bool hinted = node.Tags.ContainsKey("nine-slice");
                NineSliceResult result = NineSliceDetector.Detect(bitmap);
                if (hinted && !result.IsSliceable)
                {
                    Console.WriteLine("警告: 图层标了 sliced 但没检测出九宫 -> " + node.Name + " (" + result.Reason + ")");
                }

                string fileName = node.LayerId + ".png";
                PngEncoder.Write(Path.Combine(directory, fileName), result.Sprite);
                index.Add(JsonValue.Object()
                    .Set("id", JsonValue.Number(node.LayerId))
                    .Set("name", JsonValue.String(node.Name))
                    .Set("file", JsonValue.String(fileName))
                    .Set("type", JsonValue.String(node.Type.ToContract()))
                    .Set("hinted", JsonValue.Bool(hinted))
                    .Set("width", JsonValue.Number(result.Sprite.Width))
                    .Set("height", JsonValue.Number(result.Sprite.Height))
                    .Set("border", JsonValue.Array()
                        .Add(JsonValue.Number(result.Border.Left))
                        .Add(JsonValue.Number(result.Border.Bottom))
                        .Add(JsonValue.Number(result.Border.Right))
                        .Add(JsonValue.Number(result.Border.Top)))
                    .Set("sourceRect", JsonValue.Array()
                        .Add(JsonValue.Number(result.SourceRect.X))
                        .Add(JsonValue.Number(result.SourceRect.Y))
                        .Add(JsonValue.Number(result.SourceRect.Width))
                        .Add(JsonValue.Number(result.SourceRect.Height)))
                    .Set("uniform", JsonValue.Bool(result.IsUniform))
                    .Set("reason", JsonValue.String(result.Reason)));
                written++;
            }

            File.WriteAllText(Path.Combine(directory, "index.json"), index.ToJsonString(true), new UTF8Encoding(false));
            return written;
        }

        private static string Next(string[] args, ref int index)
        {
            return index + 1 < args.Length ? args[++index] : null;
        }

        /// <summary>打印语义层结果：类型分布 + 诊断统计（可选打印节点树）。</summary>
        private static void PrintSemantics(UiDocument document, bool showNodes)
        {
            Console.WriteLine();
            Console.WriteLine("节点      : " + document.Stats["nodes"] + " 个（组 " + document.Stats["groups"] + "）");
            Console.WriteLine("类型分布  : " + document.Stats["types"]);
            Console.WriteLine("来源      : 标签 " + document.Stats["tagged"] + " / 推断 " + document.Stats["inferred"] +
                              " / 覆盖 " + document.Stats["overridden"] + " / 引用 " + document.Stats["references"] +
                              " / 隐藏 " + document.Stats["hidden"] + " / 忽略 " + document.Stats["ignored"]);
            Console.WriteLine("诊断      : " + document.Diagnostics.Count +
                              " (E" + document.CountSeverity(DiagnosticSeverity.Error) +
                              "/W" + document.CountSeverity(DiagnosticSeverity.Warning) +
                              "/I" + document.CountSeverity(DiagnosticSeverity.Info) + ")");

            if (!showNodes)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("节点树:");
            PrintNode(document.Root, 0);
        }

        private static void PrintNode(UiNode node, int depth)
        {
            if (node == null)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append(new string(' ', depth * 2));
            builder.Append('[').Append(node.Type.ToContract());
            if (node.Role != UiRole.None)
            {
                builder.Append(':').Append(node.Role.ToContract());
            }

            builder.Append("] ").Append(node.Name);
            builder.Append("  ").Append(node.Rect);
            if (!node.Visible)
            {
                builder.Append("  (隐藏)");
            }

            if (node.Id.Length > 0 && depth > 0)
            {
                builder.Append("  #").Append(node.Id);
            }

            Console.WriteLine(builder.ToString());
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                PrintNode(node.Children[i], depth + 1);
            }
        }
    }
}
