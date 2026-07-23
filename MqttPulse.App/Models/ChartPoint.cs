namespace MqttPulse.App.Models;

public sealed record ChartPoint(
    DateTimeOffset Timestamp,
    double Value);
