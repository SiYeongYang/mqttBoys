using MqttPulse.App.ViewModels;
using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class HistoryViewModelTests
{
    [TestMethod]
    public void HistoryItemShowsDeltaFromNewerMessage()
    {
        var newer = Message("Edge/data/device", "{\"state\":\"new\"}", "2026-06-18T20:09:42.109+09:00");
        var older = Message("Edge/data/device", "{\"state\":\"old\"}", "2026-06-18T20:09:41.649+09:00");

        var latestRow = new HistoryItemViewModel(newer, previousNewer: null);
        var olderRow = new HistoryItemViewModel(older, previousNewer: newer);

        Assert.AreEqual("2026-06-18 20:09:42.109", latestRow.ReceivedAtWithDeltaText);
        Assert.AreEqual("2026-06-18 20:09:41.649 (-0.46 seconds)", olderRow.ReceivedAtWithDeltaText);
    }

    [TestMethod]
    public void SelectingHistoryKeepsLatestValueAndShowsSelectedPayloadSeparately()
    {
        using var viewModel = new MainViewModel();
        var topic = new TopicViewModel("device", "Edge/data/device", historyCapacity: 10);
        var latest = Message("Edge/data/device", "{\"state\":\"latest\"}", "2026-06-18T20:09:42.109+09:00");
        var older = Message("Edge/data/device", "{\"state\":\"older\"}", "2026-06-18T20:09:41.649+09:00");

        topic.Record(older, isLeaf: true, leafTopicWasNew: true);
        topic.Record(latest, isLeaf: true, leafTopicWasNew: false);

        viewModel.SelectedTopic = topic;
        viewModel.SelectedHistoryItem = viewModel.SelectedTopicHistory.Single(x => ReferenceEquals(x.Message, older));

        StringAssert.Contains(viewModel.ValuePayloadText, "\"state\": \"latest\"");
        StringAssert.Contains(viewModel.SelectedPayloadText, "\"state\": \"older\"");
    }

    [TestMethod]
    public void TemporaryHistorySelectionClearDoesNotEraseSelectedPayload()
    {
        using var viewModel = new MainViewModel();
        var topic = new TopicViewModel("device", "Edge/data/device", historyCapacity: 10);
        var older = Message("Edge/data/device", "{\"state\":\"older\"}", "2026-06-18T20:09:41.649+09:00");

        topic.Record(older, isLeaf: true, leafTopicWasNew: true);
        viewModel.SelectedTopic = topic;
        viewModel.SelectedHistoryItem = viewModel.SelectedTopicHistory.Single();

        viewModel.SelectedHistoryItem = null;

        StringAssert.Contains(viewModel.SelectedPayloadText, "\"state\": \"older\"");
    }

    [TestMethod]
    public void SelectedTopicShowsAverageHistoryPeriod()
    {
        using var viewModel = new MainViewModel();
        var topic = new TopicViewModel("device", "Edge/data/device", historyCapacity: 10);

        topic.Record(Message("Edge/data/device", "{\"state\":1}", "2026-06-18T20:09:41.000+09:00"), isLeaf: true, leafTopicWasNew: true);
        topic.Record(Message("Edge/data/device", "{\"state\":2}", "2026-06-18T20:09:41.010+09:00"), isLeaf: true, leafTopicWasNew: false);
        topic.Record(Message("Edge/data/device", "{\"state\":3}", "2026-06-18T20:09:41.030+09:00"), isLeaf: true, leafTopicWasNew: false);

        viewModel.SelectedTopic = topic;

        Assert.AreEqual("Avg 15 ms", viewModel.SelectedTopicAveragePeriodText);
    }

    [TestMethod]
    public void PauseCommandUsesEnglishLabelAndFreezesTheCombinedDetailView()
    {
        using var viewModel = new MainViewModel();

        Assert.AreEqual("Pause", viewModel.HistoryPauseButtonText);

        viewModel.ToggleHistoryPauseCommand.Execute(null);

        Assert.IsTrue(viewModel.HistoryPaused);
        Assert.AreEqual("Resume", viewModel.HistoryPauseButtonText);
    }

    private static MqttMessageSnapshot Message(string topic, string payload, string receivedAt) =>
        new(topic, payload, DateTimeOffset.Parse(receivedAt), Qos: 0, Retain: false);
}
