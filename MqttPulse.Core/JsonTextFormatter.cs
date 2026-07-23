using System.Text.Encodings.Web;
using System.Text.Json;

namespace MqttPulse.Core;

public static class JsonTextFormatter
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool TryFormat(
        string text,
        bool indented,
        out string formatted,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            formatted = string.Empty;
            error = "JSON을 입력하세요.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            formatted = JsonSerializer.Serialize(
                document.RootElement,
                indented ? IndentedOptions : CompactOptions);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            formatted = string.Empty;
            error = BuildError(ex);
            return false;
        }
    }

    private static string BuildError(JsonException exception)
    {
        var line = exception.LineNumber is { } lineNumber ? lineNumber + 1 : 0;
        var column = exception.BytePositionInLine is { } bytePosition ? bytePosition + 1 : 0;
        if (line > 0 && column > 0)
        {
            return $"유효하지 않은 JSON입니다. {line}행 {column}열을 확인하세요.";
        }

        return "유효하지 않은 JSON입니다.";
    }
}
