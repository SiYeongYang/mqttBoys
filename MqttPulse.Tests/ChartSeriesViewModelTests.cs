using System.Windows.Media;
using MqttPulse.App.ViewModels;
using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class ChartSeriesViewModelTests
{
    [TestMethod]
    public void ChartAccumulatesNewPointsAndSkipsMessagesWhilePaused()
    {
        var topic = new TopicViewModel("device", "Edge/data/device", historyCapacity: 20);
        var metric = new JsonScalarMetric("/value", "$.value", JsonScalarKind.Number);
        var chart = new ChartSeriesViewModel(
            topic.FullTopic,
            metric,
            Brushes.Orange,
            _ => topic,
            _ => { });

        topic.Record(Message(topic.FullTopic, 1, 0), isLeaf: true, leafTopicWasNew: true);
        chart.Refresh();
        Assert.HasCount(1, chart.DisplayPoints);

        topic.Record(Message(topic.FullTopic, 2, 1), isLeaf: true, leafTopicWasNew: false);
        topic.Record(Message(topic.FullTopic, 3, 2), isLeaf: true, leafTopicWasNew: false);
        chart.Refresh();
        Assert.HasCount(3, chart.DisplayPoints);
        Assert.AreEqual(3d, chart.DisplayPoints[^1].Value);

        chart.SetPaused(true);
        topic.Record(Message(topic.FullTopic, 4, 3), isLeaf: true, leafTopicWasNew: false);
        chart.Refresh();
        Assert.HasCount(3, chart.DisplayPoints);
        StringAssert.Contains(chart.StatusText, "Paused");

        chart.SetPaused(false);
        topic.Record(Message(topic.FullTopic, 5, 4), isLeaf: true, leafTopicWasNew: false);
        chart.Refresh();
        Assert.HasCount(4, chart.DisplayPoints);
        Assert.AreEqual(5d, chart.DisplayPoints[^1].Value);
    }

    private static MqttMessageSnapshot Message(string topic, double value, int milliseconds) =>
        new(
            topic,
            $"{{\"value\":{value}}}",
            DateTimeOffset.Parse("2026-07-23T20:00:00+09:00").AddMilliseconds(milliseconds),
            Qos: 0,
            Retain: false);
}
