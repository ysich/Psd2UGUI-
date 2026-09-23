using System.Collections.Generic;
using Psd2Ugui.Core.Json;

namespace Psd2Ugui.Core.Contract
{
    /// <summary>
    /// 契约与 JSON 的互转。字段顺序固定，保证同一份 PSD 每次导出的契约文本一致。
    /// </summary>
    public static class ContractJson
    {
        public static string ToJsonText(UiDocument document, bool indented = true)
        {
            return ToJson(document).ToJsonString(indented);
        }

        public static JsonValue ToJson(UiDocument document)
        {
            var root = JsonValue.Object();
            if (document == null)
            {
                return root;
            }

            root.Set("schemaVersion", JsonValue.String(document.SchemaVersion));
            root.Set("generator", JsonValue.String(document.Generator));
            root.Set("document", MetaToJson(document.Document));

            var resources = JsonValue.Array();
            for (int i = 0; i < document.Resources.Count; i++)
            {
                resources.Add(ResourceToJson(document.Resources[i]));
            }

            root.Set("resources", resources);

            var diagnostics = JsonValue.Array();
            for (int i = 0; i < document.Diagnostics.Count; i++)
            {
                diagnostics.Add(DiagnosticToJson(document.Diagnostics[i]));
            }

            root.Set("diagnostics", diagnostics);
            root.Set("root", document.Root == null ? JsonValue.Null : NodeToJson(document.Root));

            if (document.Stats.Count > 0)
            {
                var stats = JsonValue.Object();
                foreach (KeyValuePair<string, string> pair in document.Stats)
                {
                    stats.Set(pair.Key, JsonValue.String(pair.Value));
                }

                root.Set("stats", stats);
            }

            return root;
        }

        public static UiDocument Parse(string text)
        {
            return FromJson(JsonParser.Parse(text));
        }

        public static UiDocument FromJson(JsonValue value)
        {
            var document = new UiDocument();
            if (value == null || value.IsNull)
            {
                return document;
            }

            document.SchemaVersion = value["schemaVersion"].AsString(UiDocument.CurrentSchemaVersion);
            document.Generator = value["generator"].AsString("psd2ugui");
            document.Document = MetaFromJson(value["document"]);

            JsonValue resources = value["resources"];
            for (int i = 0; i < resources.Count; i++)
            {
                document.Resources.Add(ResourceFromJson(resources[i]));
            }

            JsonValue diagnostics = value["diagnostics"];
            for (int i = 0; i < diagnostics.Count; i++)
            {
                JsonValue item = diagnostics[i];
                var diagnostic = new UiDiagnostic(
                    UiNaming.ParseSeverity(item["severity"].AsString("info")),
                    item["code"].AsString(string.Empty),
                    item["message"].AsString(string.Empty));
                diagnostic.LayerPath = item["layerPath"].AsString(string.Empty);
                diagnostic.NodeId = item["nodeId"].AsString(string.Empty);
                diagnostic.LayerId = item["layerId"].AsInt(-1);
                document.Diagnostics.Add(diagnostic);
            }

            JsonValue root = value["root"];
            if (!root.IsNull)
            {
                document.Root = NodeFromJson(root);
            }

            JsonValue stats = value["stats"];
            for (int i = 0; i < stats.Count; i++)
            {
                KeyValuePair<string, JsonValue> pair = stats.Members[i];
                document.Stats[pair.Key] = pair.Value.AsString(string.Empty);
            }

            return document;
        }

        private static JsonValue MetaToJson(PsdMeta meta)
        {
            var json = JsonValue.Object();
            if (meta == null)
            {
                return json;
            }

            json.Set("name", JsonValue.String(meta.Name));
            json.Set("fileName", JsonValue.String(meta.FileName));
            json.Set("sourcePath", JsonValue.String(meta.SourcePath));
            json.Set("module", JsonValue.String(meta.Module));
            json.Set("width", JsonValue.Number(meta.Width));
            json.Set("height", JsonValue.Number(meta.Height));
            json.Set("channelCount", JsonValue.Number(meta.ChannelCount));
            json.Set("bitDepth", JsonValue.Number(meta.BitDepth));
            json.Set("colorMode", JsonValue.Number(meta.ColorMode));
            json.Set("layerCount", JsonValue.Number(meta.LayerCount));
            json.Set("resolutionPpi", JsonValue.Number(meta.ResolutionPpi));
            json.Set("generatorVersion", JsonValue.String(meta.GeneratorVersion));
            return json;
        }

        private static PsdMeta MetaFromJson(JsonValue json)
        {
            var meta = new PsdMeta();
            if (json == null || json.IsNull)
            {
                return meta;
            }

            meta.Name = json["name"].AsString(string.Empty);
            meta.FileName = json["fileName"].AsString(string.Empty);
            meta.SourcePath = json["sourcePath"].AsString(string.Empty);
            meta.Module = json["module"].AsString("common");
            meta.Width = json["width"].AsInt();
            meta.Height = json["height"].AsInt();
            meta.ChannelCount = json["channelCount"].AsInt();
            meta.BitDepth = json["bitDepth"].AsInt(8);
            meta.ColorMode = json["colorMode"].AsInt();
            meta.LayerCount = json["layerCount"].AsInt();
            meta.ResolutionPpi = json["resolutionPpi"].AsDouble();
            meta.GeneratorVersion = json["generatorVersion"].AsString(string.Empty);
            return meta;
        }

        private static JsonValue RectToJson(UiRect rect)
        {
            var json = JsonValue.Object();
            json.Set("x", JsonValue.Number(rect.X));
            json.Set("y", JsonValue.Number(rect.Y));
            json.Set("width", JsonValue.Number(rect.Width));
            json.Set("height", JsonValue.Number(rect.Height));
            return json;
        }

        private static UiRect RectFromJson(JsonValue json)
        {
            if (json == null || json.IsNull)
            {
                return UiRect.Empty;
            }

            return new UiRect(json["x"].AsDouble(), json["y"].AsDouble(), json["width"].AsDouble(),
                json["height"].AsDouble());
        }

        private static JsonValue BorderToJson(UiBorder border)
        {
            var json = JsonValue.Object();
            json.Set("left", JsonValue.Number(border.Left));
            json.Set("bottom", JsonValue.Number(border.Bottom));
            json.Set("right", JsonValue.Number(border.Right));
            json.Set("top", JsonValue.Number(border.Top));
            return json;
        }

        private static UiBorder BorderFromJson(JsonValue json)
        {
            if (json == null || json.IsNull)
            {
                return null;
            }

            return new UiBorder(json["left"].AsInt(), json["bottom"].AsInt(), json["right"].AsInt(),
                json["top"].AsInt());
        }

        private static JsonValue ColorToJson(UiColor color)
        {
            var json = JsonValue.Object();
            json.Set("r", JsonValue.Number(color.R));
            json.Set("g", JsonValue.Number(color.G));
            json.Set("b", JsonValue.Number(color.B));
            json.Set("a", JsonValue.Number(color.A));
            return json;
        }

        private static UiColor ColorFromJson(JsonValue json)
        {
            return UiColor.FromJson(json);
        }

        private static JsonValue ResourceToJson(UiResource resource)
        {
            var json = JsonValue.Object();
            json.Set("id", JsonValue.String(resource.Id));
            json.Set("kind", JsonValue.String(resource.Kind.ToContract()));
            json.Set("name", JsonValue.String(resource.Name));
            json.Set("module", JsonValue.String(resource.Module));
            json.Set("fileName", JsonValue.String(resource.FileName));
            json.Set("path", JsonValue.String(resource.ContractPath));
            json.Set("contentHash", JsonValue.String(resource.ContentHash));
            json.Set("width", JsonValue.Number(resource.Width));
            json.Set("height", JsonValue.Number(resource.Height));
            json.SetIfNotNull("border", resource.Border == null ? null : BorderToJson(resource.Border));
            json.Set("sourceLayerId", JsonValue.Number(resource.SourceLayerId));
            json.Set("sourceLayerPath", JsonValue.String(resource.SourceLayerPath));
            json.Set("sourceRect", RectToJson(resource.SourceRect));
            json.Set("shared", JsonValue.Bool(resource.Shared));
            return json;
        }

        private static UiResource ResourceFromJson(JsonValue json)
        {
            var resource = new UiResource();
            if (json == null || json.IsNull)
            {
                return resource;
            }

            resource.Id = json["id"].AsString(string.Empty);
            resource.Kind = UiNaming.ParseResourceKind(json["kind"].AsString("sprite"));
            resource.Name = json["name"].AsString(string.Empty);
            resource.Module = json["module"].AsString("common");
            resource.FileName = json["fileName"].AsString(string.Empty);
            resource.ContentHash = json["contentHash"].AsString(string.Empty);
            resource.Width = json["width"].AsInt();
            resource.Height = json["height"].AsInt();
            resource.Border = BorderFromJson(json["border"]);
            resource.SourceLayerId = json["sourceLayerId"].AsInt(-1);
            resource.SourceLayerPath = json["sourceLayerPath"].AsString(string.Empty);
            resource.SourceRect = RectFromJson(json["sourceRect"]);
            resource.Shared = json["shared"].AsBool();
            return resource;
        }

        private static JsonValue DiagnosticToJson(UiDiagnostic diagnostic)
        {
            var json = JsonValue.Object();
            json.Set("severity", JsonValue.String(diagnostic.Severity.ToContract()));
            json.Set("code", JsonValue.String(diagnostic.Code));
            json.Set("message", JsonValue.String(diagnostic.Message));
            json.SetIfNotNull("layerPath",
                string.IsNullOrEmpty(diagnostic.LayerPath) ? null : JsonValue.String(diagnostic.LayerPath));
            json.SetIfNotNull("nodeId",
                string.IsNullOrEmpty(diagnostic.NodeId) ? null : JsonValue.String(diagnostic.NodeId));
            json.Set("layerId", JsonValue.Number(diagnostic.LayerId));
            return json;
        }

        private static JsonValue EffectToJson(UiEffect effect)
        {
            var json = JsonValue.Object();
            json.Set("kind", JsonValue.String(effect.Kind));
            json.Set("enabled", JsonValue.Bool(effect.Enabled));
            json.Set("size", JsonValue.Number(effect.Size));
            json.Set("distance", JsonValue.Number(effect.Distance));
            json.Set("angle", JsonValue.Number(effect.Angle));
            json.Set("choke", JsonValue.Number(effect.Choke));
            json.Set("color", ColorToJson(effect.Color));
            json.Set("opacity", JsonValue.Number(effect.Opacity));
            if (effect.HasSecondColor)
            {
                json.Set("secondColor", ColorToJson(effect.SecondColor));
            }

            json.SetIfNotNull("blendMode",
                string.IsNullOrEmpty(effect.BlendMode) ? null : JsonValue.String(effect.BlendMode));
            json.SetIfNotNull("style", string.IsNullOrEmpty(effect.Style) ? null : JsonValue.String(effect.Style));
            return json;
        }

        private static UiEffect EffectFromJson(JsonValue json)
        {
            var effect = new UiEffect();
            effect.Kind = json["kind"].AsString(string.Empty);
            effect.Enabled = json["enabled"].AsBool(true);
            effect.Size = json["size"].AsDouble();
            effect.Distance = json["distance"].AsDouble();
            effect.Angle = json["angle"].AsDouble();
            effect.Choke = json["choke"].AsDouble();
            effect.Color = ColorFromJson(json["color"]);
            effect.Opacity = json["opacity"].AsDouble(1d);
            JsonValue second = json["secondColor"];
            if (!second.IsNull)
            {
                effect.HasSecondColor = true;
                effect.SecondColor = ColorFromJson(second);
            }

            effect.BlendMode = json["blendMode"].AsString(string.Empty);
            effect.Style = json["style"].AsString(string.Empty);
            return effect;
        }

        private static JsonValue TextToJson(UiTextInfo text)
        {
            var json = JsonValue.Object();
            json.Set("content", JsonValue.String(text.Content));
            json.Set("fontSize", JsonValue.Number(text.FontSize));
            json.Set("color", ColorToJson(text.Color));
            json.Set("fontKey", JsonValue.String(text.FontKey));
            json.Set("fontName", JsonValue.String(text.FontName));
            json.Set("align", JsonValue.String(text.Align));
            json.Set("wordWrap", JsonValue.Bool(text.WordWrap));
            json.Set("lineSpacing", JsonValue.Number(text.LineSpacing));
            json.Set("tracking", JsonValue.Number(text.Tracking));
            json.Set("pointText", JsonValue.Bool(text.PointText));
            return json;
        }

        private static UiTextInfo TextFromJson(JsonValue json)
        {
            var text = new UiTextInfo();
            text.HasValue = true;
            text.Content = json["content"].AsString(string.Empty);
            text.FontSize = json["fontSize"].AsDouble();
            text.Color = ColorFromJson(json["color"]);
            text.FontKey = json["fontKey"].AsString(string.Empty);
            text.FontName = json["fontName"].AsString(string.Empty);
            text.Align = json["align"].AsString("center");
            text.WordWrap = json["wordWrap"].AsBool(true);
            text.LineSpacing = json["lineSpacing"].AsDouble();
            text.Tracking = json["tracking"].AsDouble();
            text.PointText = json["pointText"].AsBool();
            return text;
        }

        private static JsonValue NodeToJson(UiNode node)
        {
            var json = JsonValue.Object();
            json.Set("id", JsonValue.String(node.Id));
            json.Set("name", JsonValue.String(node.Name));
            json.Set("layerPath", JsonValue.String(node.LayerPath));
            json.Set("layerId", JsonValue.Number(node.LayerId));
            json.Set("type", JsonValue.String(node.Type.ToContract()));
            json.Set("role", JsonValue.String(node.Role.ToContract()));
            json.Set("rect", RectToJson(node.Rect));
            json.Set("visible", JsonValue.Bool(node.Visible));
            json.Set("opacity", JsonValue.Number(node.Opacity));
            if (node.Clipping)
            {
                json.Set("clipping", JsonValue.Bool(true));
            }

            if (!string.IsNullOrEmpty(node.SectionKind))
            {
                json.Set("sectionKind", JsonValue.String(node.SectionKind));
            }

            if (!string.IsNullOrEmpty(node.ResourceId))
            {
                json.Set("resourceId", JsonValue.String(node.ResourceId));
            }

            if (node.Border != null)
            {
                json.Set("border", BorderToJson(node.Border));
            }

            if (node.HasFill)
            {
                json.Set("fill", ColorToJson(node.Fill));
            }

            if (node.Text != null && node.Text.HasValue)
            {
                json.Set("text", TextToJson(node.Text));
            }

            if (node.Effects.Count > 0)
            {
                var effects = JsonValue.Array();
                for (int i = 0; i < node.Effects.Count; i++)
                {
                    effects.Add(EffectToJson(node.Effects[i]));
                }

                json.Set("effects", effects);
            }

            if (node.Tags.Count > 0)
            {
                var tags = JsonValue.Object();
                foreach (KeyValuePair<string, string> pair in node.Tags)
                {
                    tags.Set(pair.Key, JsonValue.String(pair.Value));
                }

                json.Set("tags", tags);
            }

            var children = JsonValue.Array();
            for (int i = 0; i < node.Children.Count; i++)
            {
                children.Add(NodeToJson(node.Children[i]));
            }

            json.Set("children", children);
            return json;
        }

        private static UiNode NodeFromJson(JsonValue json)
        {
            var node = new UiNode();
            if (json == null || json.IsNull)
            {
                return node;
            }

            node.Id = json["id"].AsString(string.Empty);
            node.Name = json["name"].AsString(string.Empty);
            node.LayerPath = json["layerPath"].AsString(string.Empty);
            node.LayerId = json["layerId"].AsInt(-1);
            node.Type = UiNaming.ParseElementType(json["type"].AsString("none"));
            node.Role = UiNaming.ParseRole(json["role"].AsString("none"));
            node.Rect = RectFromJson(json["rect"]);
            node.Visible = json["visible"].AsBool(true);
            node.Opacity = json["opacity"].AsDouble(1d);
            node.Clipping = json["clipping"].AsBool();
            node.SectionKind = json["sectionKind"].AsString(string.Empty);
            node.ResourceId = json["resourceId"].IsNull ? null : json["resourceId"].AsString(string.Empty);
            node.Border = BorderFromJson(json["border"]);
            JsonValue fill = json["fill"];
            if (!fill.IsNull)
            {
                node.HasFill = true;
                node.Fill = ColorFromJson(fill);
            }

            JsonValue text = json["text"];
            if (!text.IsNull)
            {
                node.Text = TextFromJson(text);
            }

            JsonValue effects = json["effects"];
            for (int i = 0; i < effects.Count; i++)
            {
                node.Effects.Add(EffectFromJson(effects[i]));
            }

            JsonValue tags = json["tags"];
            for (int i = 0; i < tags.Count; i++)
            {
                KeyValuePair<string, JsonValue> pair = tags.Members[i];
                node.Tags[pair.Key] = pair.Value.AsString(string.Empty);
            }

            JsonValue children = json["children"];
            for (int i = 0; i < children.Count; i++)
            {
                node.Children.Add(NodeFromJson(children[i]));
            }

            return node;
        }
    }
}
