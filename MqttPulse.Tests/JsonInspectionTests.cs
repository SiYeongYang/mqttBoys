using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class JsonInspectionTests
{
    [TestMethod]
    public void AsciiConversionPreservesJsonStructureOtherFieldsAndChartSource()
    {
        const string json = "{\"SEND_TIME\":1788741127078,\"HEALTH\":[{\"test\":\"18806\"}],\"other\":18806}";
        var fields = new Dictionary<string, AsciiByteOrder> { ["/HEALTH/0/test"] = AsciiByteOrder.LittleEndian };
        Assert.IsTrue(JsonDisplayFormatter.TryBuild(json, out var lines, asciiFields: fields));
        var output = string.Join(Environment.NewLine, lines.Select(line => line.Text));
        using var doc = System.Text.Json.JsonDocument.Parse(output);
        Assert.AreEqual("vI", doc.RootElement.GetProperty("HEALTH")[0].GetProperty("test").GetString());
        Assert.AreEqual(18806, doc.RootElement.GetProperty("other").GetInt32());
        var converted = lines.Single(line => line.AsciiTarget?.Pointer == "/HEALTH/0/test");
        Assert.AreEqual(JsonScalarKind.Number, converted.Metric!.Kind);
        Assert.IsTrue(JsonScalarExtractor.TryRead(json, converted.Metric, out var value));
        Assert.AreEqual(18806d, value);
    }

    [TestMethod]
    public void AsciiArrayConversionCombinesOnlySelectedArrayAndEscapedPath()
    {
        const string json = "{\"a/b~c\":[18501,19532,20257],\"other\":[18806]}";
        var fields = new Dictionary<string, AsciiByteOrder> { ["/a~1b~0c"] = AsciiByteOrder.BigEndian };
        Assert.IsTrue(JsonDisplayFormatter.TryBuild(json, out var lines, asciiFields: fields));
        using var doc = System.Text.Json.JsonDocument.Parse(string.Join(Environment.NewLine, lines.Select(line => line.Text)));
        Assert.AreEqual("HELLO!", doc.RootElement.GetProperty("a/b~c").GetString());
        Assert.AreEqual(18806, doc.RootElement.GetProperty("other")[0].GetInt32());
    }

    [TestMethod]
    public void UnsupportedFieldStaysOriginalAndControlBytesRemainValidJson()
    {
        var fields = new Dictionary<string, AsciiByteOrder>
        {
            ["/test"] = AsciiByteOrder.LittleEndian,
            ["/fraction"] = AsciiByteOrder.BigEndian
        };
        Assert.IsTrue(JsonDisplayFormatter.TryBuild("{\"test\":\"1\",\"fraction\":12.5}", out var lines, asciiFields: fields));
        using var doc = System.Text.Json.JsonDocument.Parse(string.Join(Environment.NewLine, lines.Select(line => line.Text)));
        Assert.AreEqual("\u0001\u0000", doc.RootElement.GetProperty("test").GetString());
        Assert.AreEqual(12.5m, doc.RootElement.GetProperty("fraction").GetDecimal());
    }

    [TestMethod]
    public void StructureBuilderCreatesTypedExpandableNodes()
    {
        const string json = """
            {
              "items": [
                { "name": "motor", "value": 42 }
              ],
              "active": true,
              "optional": null
            }
            """;

        var success = JsonStructureBuilder.TryBuild(json, out var root, out var error);

        Assert.IsTrue(success, error);
        Assert.IsNotNull(root);
        Assert.AreEqual(JsonStructureKind.Object, root.Kind);
        Assert.HasCount(3, root.Children);
        Assert.AreEqual(JsonStructureKind.Array, root.Children[0].Kind);
        Assert.AreEqual(JsonStructureKind.Object, root.Children[0].Children[0].Kind);
        Assert.AreEqual(JsonStructureKind.Number, root.Children[0].Children[0].Children[1].Kind);
        Assert.AreEqual("42", root.Children[0].Children[0].Children[1].Value);
        Assert.AreEqual(JsonStructureKind.Boolean, root.Children[1].Kind);
        Assert.AreEqual(JsonStructureKind.Null, root.Children[2].Kind);
    }

    [TestMethod]
    public void DisplayFormatterMarksOnlyChartableScalarRows()
    {
        const string json = """
            {
              "value": 42,
              "running": true,
              "numericText": "12.5",
              "label": "motor"
            }
            """;

        var success = JsonDisplayFormatter.TryBuild(json, out var lines);
        var metrics = lines
            .Where(line => line.Metric is not null)
            .Select(line => line.Metric!)
            .ToArray();

        Assert.IsTrue(success);
        Assert.HasCount(3, metrics);
        CollectionAssert.AreEquivalent(
            new[] { "$.value", "$.running", "$.numericText" },
            metrics.Select(metric => metric.DisplayPath).ToArray());
    }
}
