using System.Globalization;
using MqttPulse.Core;

namespace MqttPulse.App.ViewModels;

public sealed class PeriodCheckTopicViewModel
{
    public PeriodCheckTopicViewModel(TopicViewModel topic)
    {
        Topic = topic;
        AveragePeriodText = BuildAveragePeriodText(topic.HistoryNewestFirst());
    }

    public TopicViewModel Topic { get; }

    public string FullTopic => Topic.FullTopic;

    public int MessageCount => Topic.MessageCount;

    public string LastSeenText => Topic.LastMessage?.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty;

    public string AveragePeriodText { get; }

    private static string BuildAveragePeriodText(IReadOnlyList<MqttMessageSnapshot> history)
    {
        var statistics = MessagePeriodStatistics.Calculate(history.Select(x => x.ReceivedAt));
        return statistics.HasIntervals
            ? FormatMilliseconds(statistics.AverageMilliseconds)
            : "-";
    }

    private static string FormatMilliseconds(double milliseconds)
    {
        var format = milliseconds < 10 ? "0.##" : milliseconds < 100 ? "0.#" : "0";
        return $"{milliseconds.ToString(format, CultureInfo.InvariantCulture)} ms";
    }
}
