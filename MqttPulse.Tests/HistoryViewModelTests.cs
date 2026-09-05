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

    [TestMethod]
    public void SelectingAnotherTopicResetsPauseToLiveUpdates()
    {
        using var viewModel = new MainViewModel();
        var first = new TopicViewModel("first", "Edge/data/first", historyCapacity: 10);
        var second = new TopicViewModel("second", "Edge/data/second", historyCapacity: 10);
        first.Record(
            Message("Edge/data/first", "{\"value\":1}", "2026-06-18T20:09:41.000+09:00"),
            isLeaf: true,
            leafTopicWasNew: true);
        second.Record(
            Message("Edge/data/second", "{\"value\":2}", "2026-06-18T20:09:42.000+09:00"),
            isLeaf: true,
            leafTopicWasNew: true);

        viewModel.SelectedTopic = first;
        viewModel.ToggleHistoryPauseCommand.Execute(null);
        viewModel.SelectedTopic = second;

        Assert.IsFalse(viewModel.HistoryPaused);
        Assert.AreEqual("Pause", viewModel.HistoryPauseButtonText);
        StringAssert.Contains(viewModel.ValuePayloadText, "\"value\": 2");
    }

    [TestMethod]
    public void ValueDiffModeResetsForAnotherTopic()
    {
        using var viewModel = new MainViewModel();
        var first = new TopicViewModel("first", "Edge/data/first", historyCapacity: 10);
        var second = new TopicViewModel("second", "Edge/data/second", historyCapacity: 10);
        first.Record(
            Message("Edge/data/first", "{\"value\":1}", "2026-06-18T20:09:41.000+09:00"),
            isLeaf: true,
            leafTopicWasNew: true);
        second.Record(
            Message("Edge/data/second", "{\"value\":2}", "2026-06-18T20:09:42.000+09:00"),
            isLeaf: true,
            leafTopicWasNew: true);

        viewModel.SelectedTopic = first;
        viewModel.ShowValueDiffCommand.Execute(null);

        Assert.IsTrue(viewModel.IsValueDiffMode);
        Assert.IsFalse(viewModel.IsValueRawMode);

        viewModel.SelectedTopic = second;

        Assert.IsFalse(viewModel.IsValueDiffMode);
        Assert.IsTrue(viewModel.IsValueRawMode);
    }

    [TestMethod]
    public void AsciiModesDecodeCurrentAndSelectedRegisterValues()
    {
        using var viewModel = new MainViewModel();
        var topic = new TopicViewModel("device", "Edge/data/device", historyCapacity: 10);
        var older = Message("Edge/data/device", "18806", "2026-06-18T20:09:41.000+09:00");
        var latest = Message("Edge/data/device", "18501", "2026-06-18T20:09:42.000+09:00");
        topic.Record(older, isLeaf: true, leafTopicWasNew: true);
        topic.Record(latest, isLeaf: true, leafTopicWasNew: false);

        viewModel.SelectedTopic = topic;
        viewModel.SelectedHistoryItem = viewModel.SelectedTopicHistory.Single(
            item => ReferenceEquals(item.Message, older));
        viewModel.ToggleHistoryPauseCommand.Execute(null);
        viewModel.ShowValueAsciiCommand.Execute(null);
        viewModel.ShowSelectedAsciiCommand.Execute(null);

        Assert.IsTrue(viewModel.IsValueAsciiMode);
        Assert.IsFalse(viewModel.IsValueRawMode);
        StringAssert.Contains(viewModel.ValueDisplayText, "High byte first (BE): HE");
        StringAssert.Contains(viewModel.ValueDisplayText, "Byte-swapped (LE): EH");
        Assert.IsTrue(viewModel.IsSelectedAsciiMode);
        StringAssert.Contains(viewModel.SelectedDisplayText, "High byte first (BE): Iv");
        StringAssert.Contains(viewModel.SelectedDisplayText, "Byte-swapped (LE): vI");
    }

    [TestMethod]
    public void SelectingAnotherTopicReturnsAsciiViewsToRaw()
    {
        using var viewModel = new MainViewModel();
        var first = new TopicViewModel("first", "Edge/data/first", historyCapacity: 10);
        var second = new TopicViewModel("second", "Edge/data/second", historyCapacity: 10);
        first.Record(
            Message("Edge/data/first", "18806", "2026-06-18T20:09:41.000+09:00"),
            isLeaf: true,
            leafTopicWasNew: true);
        second.Record(
            Message("Edge/data/second", "18501", "2026-06-18T20:09:42.000+09:00"),
            isLeaf: true,
            leafTopicWasNew: true);

        viewModel.SelectedTopic = first;
        viewModel.SelectedHistoryItem = viewModel.SelectedTopicHistory.Single();
        viewModel.ToggleHistoryPauseCommand.Execute(null);
        viewModel.ShowValueAsciiCommand.Execute(null);
        viewModel.ShowSelectedAsciiCommand.Execute(null);
        viewModel.SelectedTopic = second;

        Assert.IsTrue(viewModel.IsValueRawMode);
        Assert.IsFalse(viewModel.IsValueAsciiMode);
        Assert.IsTrue(viewModel.IsSelectedRawMode);
        Assert.IsFalse(viewModel.IsSelectedAsciiMode);
        Assert.AreEqual("18501", viewModel.ValueDisplayText);
    }

    private static MqttMessageSnapshot Message(string topic, string payload, string receivedAt) =>
        new(topic, payload, DateTimeOffset.Parse(receivedAt), Qos: 0, Retain: false);

    [TestMethod]
    public void RetainedSelectionRemainsDecodableAfterHistorySelectionIsCleared()
    {
        using var viewModel = new MainViewModel();
        var message = Message("factory/device", "{\"value\":18806}", "2026-09-05T10:00:00+09:00");
        viewModel.SelectedHistoryItem = new HistoryItemViewModel(message, null);
        var original = viewModel.SelectedPayloadText;
        viewModel.SelectedHistoryItem = null;
        Assert.IsTrue(viewModel.ShowSelectedAsciiCommand.CanExecute(null));
        viewModel.ShowSelectedAsciiCommand.Execute(null);
        StringAssert.Contains(viewModel.SelectedDisplayText, "Iv");
        viewModel.ShowSelectedRawCommand.Execute(null);
        Assert.AreSame(original, viewModel.SelectedPayloadText);

        viewModel.SelectedHistoryItem = new HistoryItemViewModel(message, null);
        Assert.AreSame(original, viewModel.SelectedPayloadText, "Re-selecting the same snapshot must not format it again.");
        viewModel.ClearTopicsCommand.Execute(null);
        Assert.IsFalse(viewModel.ShowSelectedAsciiCommand.CanExecute(null));
        Assert.AreEqual(string.Empty, viewModel.SelectedDisplayText);
    }
}
