using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

namespace MqttPulse.Core;

public static class PayloadFormatter
{
    private static readonly JsonSerializerOptions DisplayJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static PayloadFormatResult Format(string payload, int previewLimit = 160, int displayLimit = 1_000_000)
    {
        if (previewLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previewLimit), "Preview limit must be greater than zero.");
        }

        if (displayLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayLimit), "Display limit must be greater than zero.");
        }

        var preview = BuildPreview(payload, previewLimit);
        var isJson = TryFormatJson(payload, out var jsonText);
        var display = isJson ? jsonText! : payload;

        var truncated = display.Length > displayLimit;
        if (truncated)
        {
            display = display[..displayLimit] + Environment.NewLine + "... truncated in viewer";
        }

        return new PayloadFormatResult(
            preview,
            display,
            isJson,
            truncated);
    }

    public static string BuildPreview(string payload, int previewLimit)
    {
        if (previewLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previewLimit), "Preview limit must be greater than zero.");
        }

        var preview = new StringBuilder(Math.Min(payload.Length, previewLimit + 1));
        var pendingSpace = false;
        foreach (var value in payload)
        {
            if (char.IsWhiteSpace(value))
            {
                pendingSpace = preview.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                preview.Append(' ');
                pendingSpace = false;
            }

            preview.Append(value);
            if (preview.Length > previewLimit)
            {
                break;
            }
        }

        if (preview.Length <= previewLimit)
        {
            return preview.ToString();
        }

        if (previewLimit <= 3)
        {
            return preview.ToString(0, previewLimit);
        }

        return preview.ToString(0, previewLimit - 3) + "...";
    }

    private static bool TryFormatJson(string payload, out string? formatted)
    {
        try
        {
            var node = JsonNode.Parse(payload);
            if (node is null)
            {
                formatted = null;
                return false;
            }

            // Some MQTT payloads embed full JSON arrays/objects inside string fields.
            // Expanding those strings keeps detail views readable instead of showing escaped quotes.
            var normalized = ExpandNestedJsonStrings(node);
            formatted = normalized.ToJsonString(DisplayJsonOptions);
            return true;
        }
        catch (JsonException)
        {
            formatted = null;
            return false;
        }
    }

    private static JsonNode ExpandNestedJsonStrings(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            var expanded = new JsonObject();
            foreach (var property in jsonObject)
            {
                expanded[property.Key] = property.Value is null
                    ? null
                    : ExpandNestedJsonStrings(property.Value);
            }

            return expanded;
        }

        if (node is JsonArray jsonArray)
        {
            var expanded = new JsonArray();
            foreach (var item in jsonArray)
            {
                expanded.Add(item is null ? null : ExpandNestedJsonStrings(item));
            }

            return expanded;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            var trimmed = stringValue.Trim();
            if (LooksLikeJson(trimmed) && TryParseJsonNode(trimmed, out var nestedNode))
            {
                return ExpandNestedJsonStrings(nestedNode!);
            }
        }

        return node.DeepClone();
    }

    private static bool LooksLikeJson(string value) =>
        value.Length >= 2
        && ((value[0] == '{' && value[^1] == '}') || (value[0] == '[' && value[^1] == ']'));

    private static bool TryParseJsonNode(string value, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(value);
            return node is not null;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }
}
