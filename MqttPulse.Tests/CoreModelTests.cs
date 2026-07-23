using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class CoreModelTests
{
    [TestMethod]
    public void BoundedMessageHistoryKeepsNewestMessagesOnly()
    {
        var history = new BoundedMessageHistory(3);

        history.Add(Message("factory/line/1", "first"));
        history.Add(Message("factory/line/1", "second"));
        history.Add(Message("factory/line/1", "third"));
        history.Add(Message("factory/line/1", "fourth"));

        var newest = history.NewestFirst().Select(x => x.PayloadText).ToArray();

        CollectionAssert.AreEqual(new[] { "fourth", "third", "second" }, newest);
        Assert.AreEqual(3, history.Count);
    }

    [TestMethod]
    public void TopicTreeIndexBuildsHierarchyAndTracksCounts()
    {
        var tree = new TopicTreeIndex(maxHistoryPerTopic: 5);

        tree.Ingest(Message("XR/data/device-a/STATE", "{\"RUN\":true}"));
        tree.Ingest(Message("XR/data/device-a/STATE", "{\"RUN\":false}"));
        tree.Ingest(Message("XR/data/device-a/HEALTH", "ok"));
        tree.Ingest(Message("Edge/data/UVCE-EWLK_01-001", "edge"));

        var xr = tree.Root.Children.Single(x => x.Name == "XR");
        var state = tree.GetTopic("XR/data/device-a/STATE");

        Assert.IsNotNull(state);
        Assert.AreEqual(3, xr.MessageCount);
        Assert.AreEqual(2, state.MessageCount);
        Assert.AreEqual("{\"RUN\":false}", state.LastMessage!.PayloadText);
        Assert.AreEqual(3, tree.TopicCount);
        Assert.AreEqual(4, tree.MessageCount);
    }

    [TestMethod]
    public void TopicSearchMatchesFullTopicCaseInsensitive()
    {
        var tree = new TopicTreeIndex(maxHistoryPerTopic: 5);

        tree.Ingest(Message("XR/data/device-a/STATE", "1"));
        tree.Ingest(Message("Edge/health/UVCE-EWLK_01-001", "2"));

        var matches = tree.SearchTopics("uvce-ewlk").Select(x => x.FullTopic).ToArray();

        CollectionAssert.AreEqual(new[] { "Edge/health/UVCE-EWLK_01-001" }, matches);
    }

    [TestMethod]
    public void PayloadFormatterKeepsPreviewShortAndFormatsJsonForDetailView()
    {
        var formatted = PayloadFormatter.Format("{\"SEND_TIME\":1781779130781,\"SERVER_RUNNING_TIME\":336,\"nested\":{\"a\":1}}", previewLimit: 30);

        Assert.IsLessThanOrEqualTo(formatted.Preview.Length, 30);
        Assert.IsTrue(formatted.IsJson);
        StringAssert.Contains(formatted.DisplayText, Environment.NewLine);
        StringAssert.Contains(formatted.DisplayText, "\"SERVER_RUNNING_TIME\"");
    }

    [TestMethod]
    public void PayloadFormatterExpandsNestedJsonStringValues()
    {
        var payload = "{\"Value\":\"[{\\u0022DISK_NAME\\u0022:\\u0022/\\u0022,\\u0022TOTAL_SIZE\\u0022:22982}]\"}";

        var formatted = PayloadFormatter.Format(payload, previewLimit: 80);

        Assert.IsTrue(formatted.IsJson);
        StringAssert.Contains(formatted.DisplayText, "\"Value\": [");
        StringAssert.Contains(formatted.DisplayText, "\"DISK_NAME\": \"/\"");
        Assert.IsFalse(formatted.DisplayText.Contains("\\u0022", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PayloadFormatterKeepsKoreanCharactersReadable()
    {
        var tagName = "\uD504\uB808\uC2A4 \uAC00\uB3D9";
        var stateKey = "\uC0C1\uD0DC";
        var stateValue = "\uC815\uC0C1";
        var payload = $"{{\"TAG_NAME\":\"{tagName}\",\"nested\":\"{{\\\"{stateKey}\\\":\\\"{stateValue}\\\"}}\"}}";

        var formatted = PayloadFormatter.Format(payload, previewLimit: 80);

        Assert.IsTrue(formatted.IsJson);
        StringAssert.Contains(formatted.DisplayText, tagName);
        StringAssert.Contains(formatted.DisplayText, stateKey);
        StringAssert.Contains(formatted.DisplayText, stateValue);
        Assert.IsFalse(formatted.DisplayText.Contains("\\uD504", StringComparison.Ordinal));
    }

    [TestMethod]
    public void JsonTextFormatterFormatsCompactsAndKeepsKoreanReadable()
    {
        const string input = "{\"상태\":\"정상\",\"value\":42,\"enabled\":true}";

        Assert.IsTrue(JsonTextFormatter.TryFormat(
            input,
            indented: true,
            out var formatted,
            out var formatError));
        Assert.AreEqual(string.Empty, formatError);
        StringAssert.Contains(formatted, Environment.NewLine);
        StringAssert.Contains(formatted, "\"상태\": \"정상\"");

        Assert.IsTrue(JsonTextFormatter.TryFormat(
            input,
            indented: false,
            out var compact,
            out var compactError));
        Assert.AreEqual(string.Empty, compactError);
        Assert.IsFalse(compact.Contains(Environment.NewLine, StringComparison.Ordinal));
        StringAssert.Contains(compact, "\"상태\":\"정상\"");
    }

    [TestMethod]
    public void JsonTextFormatterReportsInvalidInputPosition()
    {
        Assert.IsFalse(JsonTextFormatter.TryFormat(
            "{\n  \"value\": 1,\n}",
            indented: true,
            out var formatted,
            out var error));

        Assert.AreEqual(string.Empty, formatted);
        StringAssert.Contains(error, "유효하지 않은 JSON");
        StringAssert.Contains(error, "행");
    }

    [TestMethod]
    public void JsonScalarExtractorFindsAndReadsNumberBooleanAndNumericString()
    {
        const string payload =
            "{\"data\":[{\"value\":42,\"running\":true,\"speed\":\"12.5\"}],\"name\":\"line-a\"}";

        var metrics = JsonScalarExtractor.Discover(payload);
        var valueMetric = metrics.Single(metric => metric.DisplayPath == "$.data[0].value");
        var runningMetric = metrics.Single(metric => metric.DisplayPath == "$.data[0].running");
        var speedMetric = metrics.Single(metric => metric.DisplayPath == "$.data[0].speed");

        Assert.AreEqual(JsonScalarKind.Number, valueMetric.Kind);
        Assert.AreEqual(JsonScalarKind.Boolean, runningMetric.Kind);
        Assert.AreEqual(JsonScalarKind.Number, speedMetric.Kind);
        Assert.IsTrue(JsonScalarExtractor.TryRead(payload, valueMetric, out var value));
        Assert.AreEqual(42, value, 0.001);
        Assert.IsTrue(JsonScalarExtractor.TryRead(payload, runningMetric, out var running));
        Assert.AreEqual(1, running, 0.001);
        Assert.IsTrue(JsonScalarExtractor.TryRead(payload, speedMetric, out var speed));
        Assert.AreEqual(12.5, speed, 0.001);
    }

    [TestMethod]
    public void JsonScalarExtractorHandlesEscapedJsonPointerProperties()
    {
        const string payload = "{\"line/a\":{\"~value\":7}}";

        var metric = JsonScalarExtractor.Discover(payload).Single();

        Assert.AreEqual("/line~1a/~0value", metric.Pointer);
        Assert.IsTrue(JsonScalarExtractor.TryRead(payload, metric, out var value));
        Assert.AreEqual(7, value, 0.001);
    }

    [TestMethod]
    public void BoundedMessageHistoryNewestFirstCanLimitRowsWithoutAllocatingAllHistory()
    {
        var history = new BoundedMessageHistory(5);

        history.Add(Message("topic", "1"));
        history.Add(Message("topic", "2"));
        history.Add(Message("topic", "3"));

        var newest = history.NewestFirst(maxCount: 2).Select(x => x.PayloadText).ToArray();

        CollectionAssert.AreEqual(new[] { "3", "2" }, newest);
    }

    [TestMethod]
    public void BoundedMessageHistoryUsesPayloadBudgetButKeepsFiveLargeMessages()
    {
        var history = new BoundedMessageHistory(
            capacity: 20,
            maxPayloadCharacters: 30,
            minimumRetainedCount: 5);

        for (var i = 0; i < 10; i++)
        {
            history.Add(Message("topic", new string((char)('a' + i), 10)));
        }

        Assert.AreEqual(5, history.Count);
        Assert.AreEqual(50, history.PayloadCharacters);
        Assert.AreEqual(new string('j', 10), history.NewestFirst()[0].PayloadText);
        Assert.AreEqual(new string('f', 10), history.NewestFirst()[4].PayloadText);
    }

    [TestMethod]
    public void MessagePeriodStatisticsCalculatesAverageMinMaxAndDuration()
    {
        var started = DateTimeOffset.Parse("2026-06-18T21:06:00.000+09:00");
        var samples = new[]
        {
            started,
            started.AddMilliseconds(10),
            started.AddMilliseconds(40),
            started.AddMilliseconds(100)
        };

        var result = MessagePeriodStatistics.Calculate(samples);

        Assert.AreEqual(4, result.SampleCount);
        Assert.AreEqual(3, result.IntervalCount);
        Assert.AreEqual(33.33, result.AverageMilliseconds, 0.01);
        Assert.AreEqual(10, result.MinimumMilliseconds, 0.01);
        Assert.AreEqual(60, result.MaximumMilliseconds, 0.01);
        Assert.AreEqual(100, result.DurationMilliseconds, 0.01);
    }

    [TestMethod]
    public void MessagePeriodStatisticsCalculatesFromHighResolutionTicks()
    {
        var samples = new long[] { 100, 110, 140, 200 };

        var result = MessagePeriodStatistics.CalculateStopwatchTicks(samples, timestampFrequency: 1_000);

        Assert.AreEqual(4, result.SampleCount);
        Assert.AreEqual(3, result.IntervalCount);
        Assert.AreEqual(33.33, result.AverageMilliseconds, 0.01);
        Assert.AreEqual(10, result.MinimumMilliseconds, 0.01);
        Assert.AreEqual(60, result.MaximumMilliseconds, 0.01);
        Assert.AreEqual(100, result.DurationMilliseconds, 0.01);
    }

    private static MqttMessageSnapshot Message(string topic, string payload) =>
        new(topic, payload, DateTimeOffset.Parse("2026-06-18T10:38:26+09:00"), Qos: 0, Retain: false);
}
