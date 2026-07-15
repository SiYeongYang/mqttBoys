namespace MqttPulse.Core;

public sealed record PayloadFormatResult(
    string Preview,
    string DisplayText,
    bool IsJson,
    bool IsTruncated);
