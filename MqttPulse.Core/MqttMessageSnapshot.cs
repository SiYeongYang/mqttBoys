namespace MqttPulse.Core;

public sealed record MqttMessageSnapshot(
    string Topic,
    string PayloadText,
    DateTimeOffset ReceivedAt,
    int Qos,
    bool Retain,
    long ReceivedStopwatchTimestamp = 0)
{
    public int PayloadLength => PayloadText.Length;
}
