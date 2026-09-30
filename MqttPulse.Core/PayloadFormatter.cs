using System.Text.Encodings.Web;
using System.Text.Json;
using System.IO;
using System.Text;

namespace MqttPulse.Core;

public static class PayloadFormatter
{
    public static PayloadFormatResult Format(string payload, int previewLimit = 160)
    {
        if (previewLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previewLimit), "Preview limit must be greater than zero.");
        }

        var preview = BuildPreview(payload, previewLimit);
        var isJson = TryFormatJson(payload, out var jsonText);
        var display = isJson ? jsonText! : payload;

        return new PayloadFormatResult(
            preview,
            display,
            isJson,
            IsTruncated: false);
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
            using var document = JsonDocument.Parse(payload);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                WriteElement(document.RootElement, writer, 0);
            }
            formatted = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            return true;
        }
        catch (JsonException)
        {
            formatted = null;
            return false;
        }
    }

    private static void WriteElement(JsonElement element, Utf8JsonWriter writer, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(property.Value, writer, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteElement(item, writer, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = element.GetString()!;
                var trimmed = text.AsSpan().Trim();
                if (depth < 32 && trimmed.Length >= 2
                    && ((trimmed[0] == '{' && trimmed[^1] == '}') || (trimmed[0] == '[' && trimmed[^1] == ']')))
                {
                    JsonDocument? nested = null;
                    try { nested = JsonDocument.Parse(text); } catch (JsonException) { }
                    if (nested is not null)
                    {
                        using (nested) WriteElement(nested.RootElement, writer, depth + 1);
                        break;
                    }
                }
                writer.WriteStringValue(text);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
