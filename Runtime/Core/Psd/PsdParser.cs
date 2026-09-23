using System;
using System.Collections.Generic;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Psd
{
    /// <summary>
    /// 自研 PSD/PSB 解析器。只依赖 BCL，不引入任何第三方库。
    /// 解析原则：能解析的正确解析，不能解析的记为警告，绝不静默丢数据。
    /// </summary>
    public static class PsdParser
    {
        /// <summary>PSB 中长度为 8 字节的附加图层信息键。</summary>
        private static readonly string[] PsbLongLengthKeys =
        {
            "LMsk", "Lr16", "Lr32", "Layr", "Mt16", "Mt32", "Mtrn", "Alph", "FMsk", "lnk2", "FEid", "FXid",
            "PxSD", "lnkD", "lnk3"
        };

        public static PsdFile Read(byte[] data, string fileName = "")
        {
            if (data == null || data.Length < 26)
            {
                throw new PsdParseException("文件过小，不是有效的 PSD", 0);
            }

            var file = new PsdFile { Data = data, FileName = fileName ?? string.Empty };
            var reader = new PsdBinaryReader(data);

            ReadHeader(reader, file);
            ReadColorModeData(reader, file);
            ReadImageResources(reader, file);
            ReadLayerAndMask(reader, file);
            ReadCompositeHeader(reader, file);
            BuildHierarchy(file);
            return file;
        }

        private static void ReadHeader(PsdBinaryReader reader, PsdFile file)
        {
            string signature = reader.ReadSignature();
            if (signature != "8BPS")
            {
                throw new PsdParseException("文件签名不是 8BPS，实际为 '" + signature + "'", 0);
            }

            file.Version = reader.ReadUInt16();
            if (file.Version != 1 && file.Version != 2)
            {
                throw new PsdParseException("不支持的 PSD 版本: " + file.Version, 4);
            }

            reader.Skip(6);
            file.ChannelCount = reader.ReadUInt16();
            file.Height = reader.ReadInt32();
            file.Width = reader.ReadInt32();
            file.BitDepth = reader.ReadUInt16();
            file.ColorMode = reader.ReadUInt16();

            if (file.Width <= 0 || file.Height <= 0)
            {
                throw new PsdParseException("画布尺寸非法: " + file.Width + "x" + file.Height, 12);
            }

            if (file.BitDepth != 1 && file.BitDepth != 8 && file.BitDepth != 16 && file.BitDepth != 32)
            {
                file.Warnings.Add("非常规位深: " + file.BitDepth);
            }
        }

        private static void ReadColorModeData(PsdBinaryReader reader, PsdFile file)
        {
            int length = reader.ReadInt32();
            if (length < 0)
            {
                throw new PsdParseException("颜色模式数据长度非法", reader.Position);
            }

            if (length > 0)
            {
                reader.Skip(length);
            }

            if (file.Mode == PsdColorMode.Indexed)
            {
                file.Warnings.Add("索引色 PSD：需要调色板才能还原颜色，当前按 RGB 通道处理");
            }
            else if (file.Mode == PsdColorMode.Cmyk)
            {
                file.Warnings.Add("CMYK PSD：颜色会做近似转换");
            }
            else if (file.Mode != PsdColorMode.Rgb && file.Mode != PsdColorMode.Grayscale &&
                     file.Mode != PsdColorMode.Bitmap)
            {
                file.Warnings.Add("颜色模式 " + file.Mode + " 支持有限");
            }
        }

        private static void ReadImageResources(PsdBinaryReader reader, PsdFile file)
        {
            int sectionLength = reader.ReadInt32();
            if (sectionLength <= 0)
            {
                return;
            }

            int sectionEnd = reader.Position + sectionLength;
            while (reader.Position < sectionEnd - 12)
            {
                string signature = reader.ReadSignature();
                if (signature != "8BIM" && signature != "8B64" && signature != "PHUT")
                {
                    file.Warnings.Add("图像资源段签名异常: " + signature);
                    break;
                }

                var resource = new PsdImageResource { Signature = signature };
                resource.Id = reader.ReadUInt16();
                resource.Name = reader.ReadPascalString(2);
                resource.Length = reader.ReadInt32();
                resource.Offset = reader.Position;
                if (resource.Length < 0 || resource.Length > reader.Remaining)
                {
                    file.Warnings.Add("图像资源长度异常: id=" + resource.Id);
                    break;
                }

                if (resource.Id == PsdImageResourceIds.ResolutionInfo && resource.Length >= 16)
                {
                    var slice = reader.ReadSlice(resource.Length);
                    file.ResolutionPpi = slice.ReadInt32() / 65536d;
                }
                else
                {
                    reader.Skip(resource.Length);
                }

                if (resource.Length % 2 != 0 && reader.Remaining > 0)
                {
                    reader.Skip(1);
                }

                file.ImageResources.Add(resource);
            }

            if (reader.Position < sectionEnd)
            {
                reader.Seek(sectionEnd);
            }
        }

        private static void ReadLayerAndMask(PsdBinaryReader reader, PsdFile file)
        {
            long sectionLength = file.IsPsb ? reader.ReadInt64() : reader.ReadInt32();
            if (sectionLength <= 0 || sectionLength > reader.Remaining)
            {
                if (sectionLength > reader.Remaining)
                {
                    file.Warnings.Add("图层与蒙版段长度超出文件范围，已按剩余长度处理");
                    sectionLength = reader.Remaining;
                }
                else
                {
                    return;
                }
            }

            int sectionEnd = reader.Position + (int)sectionLength;
            long layerInfoLength = file.IsPsb ? reader.ReadInt64() : reader.ReadInt32();
            if (layerInfoLength > 0 && layerInfoLength <= sectionEnd - reader.Position)
            {
                int layerInfoEnd = reader.Position + (int)layerInfoLength;
                ReadLayerInfo(reader, file, file.BitDepth, layerInfoEnd);
                if (reader.Position < layerInfoEnd)
                {
                    reader.Seek(layerInfoEnd);
                }


            }
            else if (layerInfoLength > 0)
            {
                file.Warnings.Add("图层信息段长度异常，已跳过");
                reader.Seek(sectionEnd);
                return;
            }

            // 全局图层蒙版信息
            if (reader.Position + 4 <= sectionEnd)
            {
                int maskLength = reader.ReadInt32();
                if (maskLength > 0 && reader.Position + maskLength <= sectionEnd)
                {
                    reader.Skip(maskLength);
                }
                else if (maskLength > 0)
                {
                    reader.Seek(sectionEnd);
                    return;
                }
            }

            // 文档级附加图层信息：16/32 位文档的图层数据放在 Lr16 / Lr32 里
            // Photoshop 会在每块数据之后补 0~3 字节，使下一块对齐到 4 字节边界，
            // 这些补齐字节不计入长度字段，所以这里按块起点做对齐。
            int blockPivot = reader.Position;
            while (reader.Position + 12 <= sectionEnd)
            {

                string signature = reader.ReadSignature();
                if (signature != "8BIM" && signature != "8B64")
                {
                    file.Warnings.Add("文档级附加信息签名异常: " + signature);
                    break;
                }



                string key = reader.ReadAscii(4);
                long length = IsPsbLongLengthKey(key) && file.IsPsb ? reader.ReadInt64() : reader.ReadInt32();
                if (length < 0 || length > reader.Remaining)
                {
                    file.Warnings.Add("附加信息长度异常: " + key);
                    break;
                }

                var block = reader.ReadSlice((int)length);
                if ((key == "Lr16" || key == "Lr32") && file.Layers.Count == 0)
                {
                    int depth = key == "Lr16" ? 16 : 32;
                    ReadLayerInfo(block, file, depth, block.Length);
                }
                else if (key == "LMsk" || key == "Layr" || key == "Mt16" || key == "Mt32")
                {
                    file.Warnings.Add("跳过附加信息块: " + key + "（" + length + " 字节）");
                }

                int misalignment = (reader.Position - blockPivot) % 4;
                if (misalignment != 0)
                {
                    int padding = 4 - misalignment;
                    reader.Skip(Math.Min(padding, Math.Max(0, sectionEnd - reader.Position)));
                }
            }


            if (reader.Position < sectionEnd)
            {
                reader.Seek(sectionEnd);
            }
        }

        private static void ReadLayerInfo(PsdBinaryReader reader, PsdFile file, int bitDepth, int sectionEnd)
        {
            long count = file.IsPsb ? reader.ReadInt64() : reader.ReadInt16();
            if (count < 0)
            {
                count = -count;
            }

            if (count > 30000)
            {
                file.Warnings.Add("图层数量异常（" + count + "），已忽略图层信息");
                return;
            }

            var layers = new List<PsdLayer>((int)count);
            for (long i = 0; i < count; i++)
            {
                if (reader.Position >= sectionEnd)
                {
                    file.Warnings.Add("图层记录数量少于声明值，已解析 " + layers.Count + " 个");
                    break;
                }

                PsdLayer layer;
                try
                {
                    layer = ReadLayerRecord(reader, file);
                }
                catch (PsdParseException exception)
                {
                    // 记录被截断或字段错乱时停在这里，前面已解析的图层照常保留
                    file.Warnings.Add("图层记录解析失败，已停止读取剩余图层：" + exception.Message);
                    break;
                }

                layer.Index = layers.Count;
                layer.BytesPerSample = bitDepth > 8 ? (bitDepth == 16 ? 2 : 4) : 1;
                layers.Add(layer);

            }

            // 通道数据紧随图层记录，按记录顺序依次排列
            for (int i = 0; i < layers.Count; i++)
            {
                PsdLayer layer = layers[i];
                for (int c = 0; c < layer.Channels.Length; c++)
                {
                    PsdChannelInfo channel = layer.Channels[c];
                    if (channel.Length <= 0)
                    {
                        continue;
                    }

                    channel.DataOffset = reader.AbsolutePosition;
                    if (!reader.TrySkip((int)channel.Length))
                    {
                        file.Warnings.Add("图层 '" + layer.DisplayName + "' 的通道数据超出文件范围");
                        break;
                    }
                }
            }

            file.Layers = layers;
        }

        private static PsdLayer ReadLayerRecord(PsdBinaryReader reader, PsdFile file)
        {
            var layer = new PsdLayer();
            int top = reader.ReadInt32();
            int left = reader.ReadInt32();
            int bottom = reader.ReadInt32();
            int right = reader.ReadInt32();
            layer.Rect = new UiRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

            int channelCount = reader.ReadUInt16();
            layer.Channels = new PsdChannelInfo[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                var channel = new PsdChannelInfo();
                channel.Id = reader.ReadInt16();
                channel.Length = file.IsPsb ? reader.ReadInt64() : reader.ReadInt32();
                layer.Channels[i] = channel;
            }

            reader.ReadSignature(); // 混合模式签名
            layer.BlendMode = reader.ReadAscii(4);
            layer.Opacity = reader.ReadByte();
            layer.Clipping = reader.ReadByte() != 0;
            layer.Flags = reader.ReadByte();
            reader.ReadByte(); // filler

            int extraLength = reader.ReadInt32();
            if (extraLength < 0 || extraLength > reader.Remaining)
            {
                file.Warnings.Add("图层附加数据长度异常，已截断");
                extraLength = reader.Remaining;
            }

            int extraEnd = reader.Position + extraLength;
            ReadLayerMaskInfo(reader, layer, extraEnd, file);
            ReadTaggedBlocks(reader, layer, extraEnd, file);
            if (reader.Position < extraEnd)
            {
                reader.Seek(extraEnd);
            }
            else if (reader.Position > extraEnd)
            {
                // 越界说明某个子块的长度字段与实际数据不符，记录后强制对齐，
                // 否则误差会累积到后续图层，导致整份文档错位。
                file.Warnings.Add("图层 '" + layer.DisplayName + "' 附加数据越界 " +
                                  (reader.Position - extraEnd) + " 字节");
                reader.Seek(extraEnd);
            }

            return layer;
        }


        private static void ReadLayerMaskInfo(PsdBinaryReader reader, PsdLayer layer, int extraEnd, PsdFile file)
        {
            if (reader.Position + 4 > extraEnd)
            {
                return;
            }

            int maskLength = reader.ReadInt32();
            if (maskLength > 0)
            {
                layer.HasMask = true;
                int skip = Math.Min(maskLength, Math.Max(0, extraEnd - reader.Position));
                reader.Skip(skip);
            }

            if (reader.Position + 4 > extraEnd)
            {
                return;
            }

            int rangeLength = reader.ReadInt32();
            if (rangeLength > 0)
            {
                int skip = Math.Min(rangeLength, Math.Max(0, extraEnd - reader.Position));
                reader.Skip(skip);
            }
            else if (rangeLength < 0)
            {
                file.Warnings.Add("混合范围长度异常: " + rangeLength);
            }

            if (reader.Position < extraEnd)
            {
                layer.Name = reader.ReadPascalString(4);
            }
        }

        private static void ReadTaggedBlocks(PsdBinaryReader reader, PsdLayer layer, int extraEnd, PsdFile file)
        {
            while (reader.Position + 12 <= extraEnd)
            {
                string signature = reader.ReadSignature();
                if (signature != "8BIM" && signature != "8B64")
                {
                    file.Warnings.Add("图层附加信息签名异常: " + signature + "（图层 " + layer.Name + "）");
                    return;
                }

                string key = reader.ReadAscii(4);
                long length = IsPsbLongLengthKey(key) && file.IsPsb ? reader.ReadInt64() : reader.ReadInt32();
                if (length < 0 || length > extraEnd - reader.Position)
                {
                    file.Warnings.Add("图层附加信息长度异常: " + key);
                    return;
                }

                var block = reader.ReadSlice((int)length);
                try
                {
                    ApplyTaggedBlock(key, block, layer, file);
                }
                catch (PsdParseException exception)
                {
                    // 单个块解析失败只记录警告，不影响其它图层
                    file.Warnings.Add("附加信息 " + key + " 解析失败: " + exception.Message);
                }
                catch (Exception exception)
                {
                    file.Warnings.Add("附加信息 " + key + " 解析异常: " + exception.GetType().Name);
                }
            }
        }

        private static void ApplyTaggedBlock(string key, PsdBinaryReader block, PsdLayer layer, PsdFile file)
        {
            switch (key)
            {
                case PsdKeys.UnicodeName:
                    int charCount = block.ReadInt32();
                    if (charCount > 0 && charCount * 2 <= block.Remaining)
                    {
                        layer.UnicodeName = block.ReadUnicodeString(charCount);
                    }

                    break;
                case PsdKeys.LayerId:
                    layer.LayerId = block.ReadInt32();
                    break;
                case PsdKeys.SectionDivider:
                    layer.DividerType = block.Remaining >= 4 ? block.ReadInt32() : -1;
                    break;
                case PsdKeys.FillOpacity:
                    layer.FillOpacity = block.Remaining >= 4 ? (int)(block.ReadUInt32() & 0xFF) : 255;
                    break;
                case PsdKeys.BlendClipped:
                    layer.BlendClipped = block.Remaining >= 1 && block.ReadByte() != 0;
                    break;
                case PsdKeys.BlendInterior:
                    layer.BlendInteriorElements = block.Remaining >= 1 && block.ReadByte() != 0;
                    break;
                case PsdKeys.Knockout:
                    layer.Knockout = block.Remaining >= 1 && block.ReadByte() != 0;
                    break;
                case PsdKeys.Protected:
                    layer.Protected = block.Remaining >= 4 && block.ReadUInt32() != 0;
                    break;
                case PsdKeys.VectorMask:
                case PsdKeys.VectorMaskStroked:
                case PsdKeys.VectorStrokeContent:
                    layer.HasVectorMask = true;
                    break;
                case PsdKeys.Effects:
                    ReadEffects(block, layer, file);
                    break;
                case PsdKeys.EffectsOld:
                    layer.HasLegacyEffects = true;
                    break;
                case PsdKeys.TypeTool:
                    ReadTypeTool(block, layer, file);
                    break;
                case PsdKeys.SolidColor:
                    ReadFillLayer(block, layer, file, true);
                    break;
                case PsdKeys.GradientFill:
                case PsdKeys.PatternFill:
                    layer.IsFillLayer = true;
                    break;
                case PsdKeys.SmartObjectLayer:
                case PsdKeys.SmartObjectPlaced:
                    layer.HasSmartObject = true;
                    break;
                case PsdKeys.TransparencyShapesLayer:
                    layer.TransparencyShapesLayer = true;
                    break;
                case PsdKeys.Metadata:
                case PsdKeys.SheetColor:
                case PsdKeys.NameSource:
                case PsdKeys.SectionDividerSetting:
                case PsdKeys.TextEngineData:
                case PsdKeys.PixelSourceData:
                case PsdKeys.Artboard:
                case PsdKeys.ArtboardData:
                    // 已知但不影响生成的信息，直接忽略
                    break;
                default:
                    layer.UnknownTags.Add(key);
                    break;
            }
        }

        private static void ReadEffects(PsdBinaryReader block, PsdLayer layer, PsdFile file)
        {
            int start = block.Position;
            PsdDescriptor descriptor;
            try
            {
                block.ReadUInt32(); // version
                block.ReadUInt32(); // descriptor version
                descriptor = PsdDescriptorReader.ReadDescriptor(block);
            }
            catch (PsdParseException)
            {
                // 个别文件把效果描述符写在单层版本号之后，回退重试
                block.Seek(start);
                block.ReadUInt32();
                descriptor = PsdDescriptorReader.ReadDescriptor(block);
            }

            var unknown = new List<string>();
            layer.Effects.AddRange(PsdEffectsReader.Parse(descriptor, unknown));
            for (int i = 0; i < unknown.Count; i++)
            {
                file.Warnings.Add("图层 '" + layer.DisplayName + "' 存在未支持的效果类: " + unknown[i]);
            }
        }

        private static void ReadFillLayer(PsdBinaryReader block, PsdLayer layer, PsdFile file, bool solid)
        {
            layer.IsFillLayer = true;
            if (block.Remaining < 4)
            {
                return;
            }

            block.ReadUInt32(); // version
            if (block.Remaining < 4)
            {
                return;
            }

            PsdDescriptor descriptor = PsdDescriptorReader.ReadDescriptor(block);
            if (solid)
            {
                layer.HasSolidFill = true;
                layer.SolidFill = descriptor.ToColor(UiColor.White);
            }
        }

        private static void ReadTypeTool(PsdBinaryReader block, PsdLayer layer, PsdFile file)
        {
            if (block.Remaining < 2)
            {
                return;
            }

            block.ReadUInt16(); // version
            if (block.Remaining < 48)
            {
                return;
            }

            var info = new PsdTextEngineInfo();
            info.TransformXX = block.ReadDouble();
            block.ReadDouble();
            block.ReadDouble();
            info.TransformYY = block.ReadDouble();
            info.TransformX = block.ReadDouble();
            info.TransformY = block.ReadDouble();

            if (block.Remaining >= 2)
            {
                block.ReadUInt16(); // text version (50)
            }

            if (block.Remaining >= 4)
            {
                block.ReadUInt32(); // descriptor version (16)
            }

            PsdDescriptor descriptor = PsdDescriptorReader.ReadDescriptor(block);
            PsdTextEngineInfo parsed = PsdTextEngineInfo.FromDescriptor(descriptor);
            parsed.TransformXX = info.TransformXX;
            parsed.TransformYY = info.TransformYY;
            parsed.TransformX = info.TransformX;
            parsed.TransformY = info.TransformY;
            if (!parsed.HasColor)
            {
                parsed.Color = UiColor.White;
            }

            layer.Text = parsed;

            if (parsed.EngineData == null)
            {
                file.Warnings.Add("文本图层 '" + layer.DisplayName + "' 缺少文本引擎数据，字号与字体可能缺失");
            }
        }

        private static void ReadCompositeHeader(PsdBinaryReader reader, PsdFile file)
        {
            if (reader.Remaining < 2)
            {
                file.Warnings.Add("缺少合成图像数据");
                return;
            }

            file.CompositeOffset = reader.AbsolutePosition;
            file.CompositeCompression = reader.ReadUInt16();
        }

        private static bool IsPsbLongLengthKey(string key)
        {
            for (int i = 0; i < PsbLongLengthKeys.Length; i++)
            {
                if (PsbLongLengthKeys[i] == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 把扁平图层记录还原成层级。PSD 记录自下而上排列，
        /// type 3 的分隔符开启一个分组，type 1/2 的文件夹记录结束该分组。
        /// </summary>
        private static void BuildHierarchy(PsdFile file)
        {
            var stack = new List<List<PsdLayer>>();
            var root = new List<PsdLayer>();
            stack.Add(root);

            for (int i = 0; i < file.Layers.Count; i++)
            {
                PsdLayer layer = file.Layers[i];
                if (layer.IsBoundingDivider)
                {
                    stack.Add(new List<PsdLayer>());
                    continue;
                }

                if (layer.IsGroup)
                {
                    if (stack.Count > 1)
                    {
                        List<PsdLayer> children = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        layer.Children.AddRange(children);
                    }

                    stack[stack.Count - 1].Add(layer);
                    continue;
                }

                stack[stack.Count - 1].Add(layer);
            }

            // 未闭合的分组：把内容上提到父级，避免丢层
            while (stack.Count > 1)
            {
                List<PsdLayer> orphan = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                stack[stack.Count - 1].AddRange(orphan);
                file.Warnings.Add("检测到未闭合的分组，已把其中的 " + orphan.Count + " 个图层上提");
            }

            file.RootLayers = root;
            AssignParentIds(file.RootLayers, -1);
        }

        private static void AssignParentIds(List<PsdLayer> layers, int parentLayerId)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                layers[i].ParentLayerId = parentLayerId;
                AssignParentIds(layers[i].Children, layers[i].LayerId);
            }
        }
    }
}
