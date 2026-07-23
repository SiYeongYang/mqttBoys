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

    [TestMethod]
    public void SearchKeepsOnlyMatchingBranchesVisibleAndRestoresExpansion()
    {
        var root = new TopicViewModel("broker", string.Empty, historyCapacity: 10);
        var edge = root.GetOrCreateChild("Edge", "Edge", historyCapacity: 10);
        var data = edge.GetOrCreateChild("data", "Edge/data", historyCapacity: 10);
        var matching = data.GetOrCreateChild("M_001", "Edge/data/M_001", historyCapacity: 10);
        var health = edge.GetOrCreateChild("health", "Edge/health", historyCapacity: 10);
        var other = health.GetOrCreateChild("status", "Edge/health/status", historyCapacity: 10);

        edge.IsExpanded = false;
        root.ApplySearch("M_001");

        Assert.IsTrue(root.IsSearchVisible);
        Assert.IsTrue(edge.IsSearchVisible);
        Assert.IsTrue(data.IsSearchVisible);
        Assert.IsTrue(matching.IsSearchVisible);
        Assert.IsFalse(health.IsSearchVisible);
        Assert.IsFalse(other.IsSearchVisible);
        Assert.IsTrue(root.IsExpanded);
        Assert.IsTrue(edge.IsExpanded);

        root.ApplySearch(string.Empty);

        Assert.IsTrue(health.IsSearchVisible);
        Assert.IsTrue(other.IsSearchVisible);
        Assert.IsFalse(edge.IsExpanded);
    }

    [TestMethod]
    public void DeferredRecordNotificationBuildsOnlyTheLatestPayloadPreview()
    {
        var leaf = new TopicViewModel("device", "Edge/data/device", historyCapacity: 10);

        leaf.Record(Message("Edge/data/device", "{\"state\":\"old\"}"), isLeaf: true, leafTopicWasNew: true, notify: false);
        leaf.Record(Message("Edge/data/device", "{\"state\":\"latest\"}"), isLeaf: true, leafTopicWasNew: false, notify: false);
        leaf.NotifyRecordChanged();

        Assert.AreEqual(2, leaf.MessageCount);
        StringAssert.Contains(leaf.LastPayloadPreview, "latest");
        Assert.IsFalse(leaf.LastPayloadPreview.Contains("old", StringComparison.Ordinal));
    }

    private static MqttMessageSnapshot Message(string topic, string payload) =>
        new(topic, payload, DateTimeOffset.Parse("2026-06-18T20:11:35+09:00"), Qos: 0, Retain: false);
}
