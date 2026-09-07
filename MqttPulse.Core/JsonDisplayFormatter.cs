using System.Text.Encodings.Web;
using System.Text.Json;

namespace MqttPulse.Core;

public sealed record JsonDisplayLine(
    string Text,
    JsonScalarMetric? Metric,
    JsonAsciiTarget? AsciiTarget = null);

public sealed record JsonAsciiTarget(string Pointer, string DisplayPath, string OriginalValue, AsciiByteOrder? Order);

public static class JsonDisplayFormatter
{
    private const int DefaultMaxLines = 5_000;
    private const int MaxDepth = 64;
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool TryBuild(
        string text,
        out IReadOnlyList<JsonDisplayLine> lines,
        int maxLines = DefaultMaxLines,
        IReadOnlyDictionary<string, AsciiByteOrder>? asciiFields = null)
    {
        if (maxLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLines));
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var output = new List<JsonDisplayLine>();
            var complete = WriteElement(
                document.RootElement,
                output,
                pointer: string.Empty,
                displayPath: "$",
                depth: 0,
                prefix: string.Empty,
                trailingComma: false,
                maxLines, asciiFields);
            lines = complete ? output : Array.Empty<JsonDisplayLine>();
            return complete;
        }
        catch (JsonException)
        {
            lines = Array.Empty<JsonDisplayLine>();
            return false;
        }
    }

    private static bool WriteElement(
        JsonElement element,
        ICollection<JsonDisplayLine> lines,
        string pointer,
        string displayPath,
        int depth,
        string prefix,
        bool trailingComma,
        int maxLines,
        IReadOnlyDictionary<string, AsciiByteOrder>? asciiFields)
    {
        if (depth > MaxDepth || lines.Count >= maxLines)
        {
            return false;
        }

        var indentation = new string(' ', depth * 2);
        var suffix = trailingComma ? "," : string.Empty;
        JsonAsciiTarget? asciiTarget = null;
        if (element.ValueKind != JsonValueKind.Object && PackedAsciiDecoder.CanDecode(element))
        {
            AsciiByteOrder? order = asciiFields is not null && asciiFields.TryGetValue(pointer, out var found) ? found : null;
            asciiTarget = new JsonAsciiTarget(pointer, displayPath, element.GetRawText(), order);
            if (order is { } selectedOrder && PackedAsciiDecoder.TryDecode(element, selectedOrder, out var ascii))
            {
                var originalMetric = JsonScalarExtractor.TryGetScalarKind(element, out var scalarKind)
                    ? new JsonScalarMetric(pointer, displayPath, scalarKind) : null;
                lines.Add(new JsonDisplayLine(
                    $"{indentation}{prefix}{JsonSerializer.Serialize(ascii, StringOptions)}{suffix}", originalMetric, asciiTarget));
                return true;
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            lines.Add(new JsonDisplayLine($"{indentation}{prefix}{{", null));
            var properties = element.EnumerateObject().ToArray();
            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                var propertyPrefix = JsonSerializer.Serialize(property.Name, StringOptions) + ": ";
                if (!WriteElement(
                        property.Value,
                        lines,
                        pointer + "/" + JsonScalarExtractor.EscapePointerToken(property.Name),
                        JsonScalarExtractor.AppendDisplayProperty(displayPath, property.Name),
                        depth + 1,
                        propertyPrefix,
                        index < properties.Length - 1,
                        maxLines, asciiFields))
                {
                    return false;
                }
            }

            if (lines.Count >= maxLines)
            {
                return false;
            }

            lines.Add(new JsonDisplayLine($"{indentation}}}{suffix}", null));
            return true;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            lines.Add(new JsonDisplayLine($"{indentation}{prefix}[", null, asciiTarget));
            var items = element.EnumerateArray().ToArray();
            for (var index = 0; index < items.Length; index++)
            {
                if (!WriteElement(
                        items[index],
                        lines,
                        pointer + "/" + index,
                        $"{displayPath}[{index}]",
                        depth + 1,
                        prefix: string.Empty,
                        trailingComma: index < items.Length - 1,
                        maxLines, asciiFields))
                {
                    return false;
                }
            }

            if (lines.Count >= maxLines)
            {
                return false;
            }

            lines.Add(new JsonDisplayLine($"{indentation}]{suffix}", null));
            return true;
        }

        var valueText = GetScalarText(element);
        var metric = JsonScalarExtractor.TryGetScalarKind(element, out var kind)
            ? new JsonScalarMetric(pointer, displayPath, kind)
            : null;
        lines.Add(new JsonDisplayLine(
            $"{indentation}{prefix}{valueText}{suffix}",
            metric, asciiTarget));
        return lines.Count <= maxLines;
    }

    private static string GetScalarText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.Serialize(element.GetString(), StringOptions),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => element.GetRawText()
    };
}
