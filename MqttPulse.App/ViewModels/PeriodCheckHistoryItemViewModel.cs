namespace MqttPulse.App.ViewModels;

public sealed class PeriodCheckHistoryItemViewModel
{
    public PeriodCheckHistoryItemViewModel(DateTime completedAt, string topic, string summary, string resultText)
    {
        CompletedAt = completedAt;
        Topic = topic;
        Summary = summary;
        ResultText = resultText;
    }

    public DateTime CompletedAt { get; }

    public string CompletedAtText => CompletedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public string Topic { get; }

    public string Summary { get; }

    public string ResultText { get; }
}
