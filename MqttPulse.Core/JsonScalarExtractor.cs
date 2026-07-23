using System.Globalization;
using System.Text.Json;

namespace MqttPulse.Core;

public enum JsonScalarKind
{
    Number,
    Boolean
}

public sealed record JsonScalarMetric(
    string Pointer,
    string DisplayPath,
    JsonScalarKind Kind)
{
    public bool IsBoolean => Kind == JsonScalarKind.Boolean;
}

public static class JsonScalarExtractor
{
    private const int DefaultMetricLimit = 200;
    private const int MaxDepth = 64;

    public static IReadOnlyList<JsonScalarMetric> Discover(
        string payload,
        int maxMetrics = DefaultMetricLimit)
    {
        if (maxMetrics <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMetrics));
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var metrics = new List<JsonScalarMetric>(Math.Min(maxMetrics, 32));
            DiscoverElement(document.RootElement, string.Empty, "$", metrics, maxMetrics, depth: 0);
            return metrics;
        }
        catch (JsonException)
        {
            return Array.Empty<JsonScalarMetric>();
        }
    }

    public static bool TryRead(
        string payload,
        JsonScalarMetric metric,
        out double value)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var current = document.RootElement;
            if (!TryResolvePointer(ref current, metric.Pointer))
            {
                value = default;
                return false;
            }

            return TryReadScalar(current, metric.Kind, out value);
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static void DiscoverElement(
        JsonElement element,
        string pointer,
        string displayPath,
        ICollection<JsonScalarMetric> metrics,
        int maxMetrics,
        int depth)
    {
        if (metrics.Count >= maxMetrics || depth > MaxDepth)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPointer = pointer + "/" + EscapePointerToken(property.Name);
                    var propertyDisplayPath = AppendDisplayProperty(displayPath, property.Name);
                    DiscoverElement(
                        property.Value,
                        propertyPointer,
                        propertyDisplayPath,
                        metrics,
                        maxMetrics,
                        depth + 1);
                    if (metrics.Count >= maxMetrics)
                    {
                        break;
                    }
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    DiscoverElement(
                        item,
                        pointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                        $"{displayPath}[{index}]",
                        metrics,
                        maxMetrics,
                        depth + 1);
                    if (metrics.Count >= maxMetrics)
                    {
                        break;
                    }

                    index++;
                }

                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.String:
                if (TryGetScalarKind(element, out var kind))
                {
                    metrics.Add(new JsonScalarMetric(pointer, displayPath, kind));
                }
                break;
        }
    }

    private static bool TryResolvePointer(ref JsonElement element, string pointer)
    {
        if (pointer.Length == 0)
        {
            return true;
        }

        if (pointer[0] != '/')
        {
            return false;
        }

        foreach (var encodedToken in pointer[1..].Split('/'))
        {
            var token = UnescapePointerToken(encodedToken);
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (!element.TryGetProperty(token, out var property))
                {
                    return false;
                }

                element = property;
                continue;
            }

            if (element.ValueKind == JsonValueKind.Array
                && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < element.GetArrayLength())
            {
                element = element[index];
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryReadScalar(
        JsonElement element,
        JsonScalarKind kind,
        out double value)
    {
        if (kind == JsonScalarKind.Boolean)
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = element.GetBoolean() ? 1 : 0;
                return true;
            }

            if (element.ValueKind == JsonValueKind.String
                && bool.TryParse(element.GetString(), out var boolean))
            {
                value = boolean ? 1 : 0;
                return true;
            }

            value = default;
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    internal static bool TryGetScalarKind(JsonElement element, out JsonScalarKind kind)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out _))
        {
            kind = JsonScalarKind.Number;
            return true;
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            kind = JsonScalarKind.Boolean;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (bool.TryParse(text, out _))
            {
                kind = JsonScalarKind.Boolean;
                return true;
            }

            if (double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                kind = JsonScalarKind.Number;
                return true;
            }
        }

        kind = default;
        return false;
    }

    internal static string AppendDisplayProperty(string parent, string propertyName)
    {
        if (propertyName.Length > 0
            && (char.IsLetter(propertyName[0]) || propertyName[0] == '_')
            && propertyName.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            return $"{parent}.{propertyName}";
        }

        return $"{parent}['{propertyName.Replace("'", "\\'", StringComparison.Ordinal)}']";
    }

    internal static string EscapePointerToken(string token) =>
        token.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static string UnescapePointerToken(string token) =>
        token.Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
}
