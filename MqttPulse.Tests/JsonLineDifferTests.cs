using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class JsonLineDifferTests
{
    [TestMethod]
    public void CompareMarksChangedJsonLinesAndPreservesSharedLines()
    {
        const string baseline = "{\n  \"state\": \"old\",\n  \"active\": true\n}";
        const string current = "{\n  \"state\": \"new\",\n  \"active\": true\n}";

        var result = JsonLineDiffer.Compare(baseline, current);

        Assert.AreEqual(1, result.AddedLineCount);
        Assert.AreEqual(1, result.RemovedLineCount);
        Assert.IsFalse(result.IsSimplified);
        CollectionAssert.AreEqual(
            new[]
            {
                JsonDiffKind.Unchanged,
                JsonDiffKind.Removed,
                JsonDiffKind.Added,
                JsonDiffKind.Unchanged,
                JsonDiffKind.Unchanged
            },
            result.Lines.Select(line => line.Kind).ToArray());
    }

    [TestMethod]
    public void CompareUsesBoundedFallbackForVeryLargePayloads()
    {
        var baselineLines = Enumerable.Range(0, 1_100).Select(index => $"line-{index}").ToArray();
        var currentLines = baselineLines.ToArray();
        currentLines[550] = "changed";

        var result = JsonLineDiffer.Compare(
            string.Join('\n', baselineLines),
            string.Join('\n', currentLines));

        Assert.IsTrue(result.IsSimplified);
        Assert.AreEqual(1, result.AddedLineCount);
        Assert.AreEqual(1, result.RemovedLineCount);
    }
}
