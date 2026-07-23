using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class JsonInspectionTests
{
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
