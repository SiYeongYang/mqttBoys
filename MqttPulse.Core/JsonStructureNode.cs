using System.Text.Json;

namespace MqttPulse.Core;

public enum JsonStructureKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Null
}

public sealed record JsonStructureNode(
    string Name,
    JsonStructureKind Kind,
    string Value,
    IReadOnlyList<JsonStructureNode> Children)
{
    public int ChildCount => Children.Count;
}

public static class JsonStructureBuilder
{
    private const int MaxDepth = 64;
    private const int MaxValueLength = 500;
    private const int MaxNodeCount = 10_000;

    public static bool TryBuild(
        string text,
        out JsonStructureNode? root,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            root = null;
            error = "JSON을 입력하세요.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var nodeCount = 0;
            root = BuildNode(
                GetRootName(document.RootElement),
                document.RootElement,
                depth: 0,
                ref nodeCount);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            root = null;
            error = JsonTextFormatter.BuildError(ex);
            return false;
        }
    }

    private static JsonStructureNode BuildNode(
        string name,
        JsonElement element,
        int depth,
        ref int nodeCount)
    {
        nodeCount++;
        if (nodeCount > MaxNodeCount)
        {
            return TruncatedNode();
        }

        if (depth > MaxDepth)
        {
            return new JsonStructureNode(
                name,
                JsonStructureKind.String,
                "... maximum depth reached",
                Array.Empty<JsonStructureNode>());
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var objectChildren = new List<JsonStructureNode>();
                foreach (var property in element.EnumerateObject())
                {
                    if (nodeCount >= MaxNodeCount)
                    {
                        objectChildren.Add(TruncatedNode());
                        break;
                    }

                    objectChildren.Add(BuildNode(
                        property.Name,
                        property.Value,
                        depth + 1,
                        ref nodeCount));
                }

                return new JsonStructureNode(
                    name,
                    JsonStructureKind.Object,
                    string.Empty,
                    objectChildren);

            case JsonValueKind.Array:
                var arrayChildren = new List<JsonStructureNode>();
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (nodeCount >= MaxNodeCount)
                    {
                        arrayChildren.Add(TruncatedNode());
                        break;
                    }

                    arrayChildren.Add(BuildNode(
                        $"[{index}]",
                        item,
                        depth + 1,
                        ref nodeCount));
                    index++;
                }

                return new JsonStructureNode(
                    name,
                    JsonStructureKind.Array,
                    string.Empty,
                    arrayChildren);

            case JsonValueKind.String:
                return Leaf(name, JsonStructureKind.String, element.GetString() ?? string.Empty);

            case JsonValueKind.Number:
                return Leaf(name, JsonStructureKind.Number, element.GetRawText());

            case JsonValueKind.True:
            case JsonValueKind.False:
                return Leaf(name, JsonStructureKind.Boolean, element.GetBoolean() ? "true" : "false");

            default:
                return Leaf(name, JsonStructureKind.Null, "null");
        }
    }

    private static JsonStructureNode Leaf(string name, JsonStructureKind kind, string value)
    {
        var displayValue = value.Length <= MaxValueLength
            ? value
            : value[..MaxValueLength] + "...";
        return new JsonStructureNode(
            name,
            kind,
            displayValue,
            Array.Empty<JsonStructureNode>());
    }

    private static JsonStructureNode TruncatedNode() =>
        new(
            "...",
            JsonStructureKind.String,
            "remaining nodes omitted",
            Array.Empty<JsonStructureNode>());

    private static string GetRootName(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        _ => "value"
    };
}
