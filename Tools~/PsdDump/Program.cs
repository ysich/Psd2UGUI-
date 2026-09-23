using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Psd2Ugui.Core.Contract;
using Psd2Ugui.Core.Json;

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
                Console.WriteLine("用法: PsdDump <file.psd> [--json out.json]");
                return 2;
            }

            string path = args[0];
            string jsonPath = null;
            string layersPath = null;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--json")
                {
                    jsonPath = args[i + 1];
                }
                else if (args[i] == "--layers")
                {
                    layersPath = args[i + 1];
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

            if (!string.IsNullOrEmpty(jsonPath))
            {
                UiDocument document = BuildContract(file);
                File.WriteAllText(jsonPath, ContractJson.ToJsonText(document), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("已输出契约草稿: " + jsonPath);
            }

            if (!string.IsNullOrEmpty(layersPath))
            {
                File.WriteAllText(layersPath, BuildLayerJson(file).ToJsonString(true), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("已输出图层明细: " + layersPath);
            }

            return 0;
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

        private static UiDocument BuildContract(PsdFile file)
        {
            var document = new UiDocument();
            document.Document = new PsdMeta
            {
                Name = Path.GetFileNameWithoutExtension(file.FileName),
                FileName = file.FileName,
                Width = file.Width,
                Height = file.Height,
                ChannelCount = file.ChannelCount,
                BitDepth = file.BitDepth,
                ColorMode = file.ColorMode,
                LayerCount = file.Layers.Count,
                ResolutionPpi = file.ResolutionPpi
            };

            var root = new UiNode
            {
                Id = StableId.NodeId("__root__", -1),
                Name = document.Document.Name,
                LayerPath = string.Empty,
                Type = UiElementType.Group,
                Rect = new UiRect(0d, 0d, file.Width, file.Height)
            };
            document.Root = root;
            return document;
        }
    }
}
