using MqttPulse.App.ViewModels;
using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class TopicViewModelTests
{
    [TestMethod]
    public void NewTopicNodesStartCollapsedAndShowExplorerStyleParentSummary()
    {
        var root = new TopicViewModel("Edge", "Edge", historyCapacity: 10);

        root.Record(Message("Edge/data/device", "{\"ok\":true}"), isLeaf: false, leafTopicWasNew: true);

        Assert.IsFalse(root.IsExpanded);
        Assert.AreEqual("Edge", root.DisplayName);
        Assert.AreEqual("(1 topic, 1 message)", root.DetailText);
    }

    [TestMethod]
    public void LeafTopicShowsPayloadPreviewAfterEqualsSign()
    {
        var leaf = new TopicViewModel("UVCE-EWLK_01-001", "Edge/data/UVCE-EWLK_01-001", historyCapacity: 10);

        leaf.Record(Message("Edge/data/UVCE-EWLK_01-001", "{\"MessageId\":\"abc\",\"Payload\":[1,2,3]}"), isLeaf: true, leafTopicWasNew: true);

        Assert.AreEqual("UVCE-EWLK_01-001", leaf.DisplayName);
        StringAssert.StartsWith(leaf.DetailText, "= {\"MessageId\":\"abc\"");
    }

    private static MqttMessageSnapshot Message(string topic, string payload) =>
        new(topic, payload, DateTimeOffset.Parse("2026-06-18T20:11:35+09:00"), Qos: 0, Retain: false);
}
