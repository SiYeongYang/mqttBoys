using MqttPulse.Core;
using System.Globalization;

namespace MqttPulse.App.ViewModels;

public sealed class HistoryItemViewModel
{
    private readonly string _deltaText;

    public HistoryItemViewModel(MqttMessageSnapshot message, MqttMessageSnapshot? previousNewer)
    {
        Message = message;
        _deltaText = BuildDeltaText(message, previousNewer);
        PayloadPreview = PayloadFormatter.BuildPreview(message.PayloadText, previewLimit: 180);
    }

    public MqttMessageSnapshot Message { get; }

    public string ReceivedAtText => Message.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss.fff");

    public string ReceivedAtWithDeltaText => ReceivedAtText + _deltaText;

    public string PayloadPreview { get; }

    public string MetaText => $"QoS {Message.Qos} | {(Message.Retain ? "retain" : "live")} | {Message.PayloadLength:N0} chars";

    private static string BuildDeltaText(MqttMessageSnapshot message, MqttMessageSnapshot? previousNewer)
    {
        if (previousNewer is null)
        {
            return string.Empty;
        }

        var seconds = (message.ReceivedAt - previousNewer.ReceivedAt).TotalSeconds;
        return string.Format(CultureInfo.InvariantCulture, " ({0:0.##} seconds)", seconds);
    }
}