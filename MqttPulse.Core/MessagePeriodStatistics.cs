namespace MqttPulse.Core;

public sealed record MessagePeriodStatisticsResult(
    int SampleCount,
    int IntervalCount,
    double AverageMilliseconds,
    double MinimumMilliseconds,
    double MaximumMilliseconds,
    double DurationMilliseconds)
{
    public bool HasIntervals => IntervalCount > 0;
}

public static class MessagePeriodStatistics
{
    public static MessagePeriodStatisticsResult Calculate(IEnumerable<DateTimeOffset> receivedAtSamples)
    {
        var samples = receivedAtSamples.OrderBy(x => x).ToArray();
        if (samples.Length < 2)
        {
            return new MessagePeriodStatisticsResult(samples.Length, 0, 0, 0, 0, 0);
        }

        var intervals = new double[samples.Length - 1];
        for (var i = 1; i < samples.Length; i++)
        {
            intervals[i - 1] = (samples[i] - samples[i - 1]).TotalMilliseconds;
        }

        return new MessagePeriodStatisticsResult(
            samples.Length,
            intervals.Length,
            intervals.Average(),
            intervals.Min(),
            intervals.Max(),
            (samples[^1] - samples[0]).TotalMilliseconds);
    }

    public static MessagePeriodStatisticsResult CalculateStopwatchTicks(IEnumerable<long> timestampSamples, long timestampFrequency)
    {
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency), "Timestamp frequency must be greater than zero.");
        }

        var samples = timestampSamples.Order().ToArray();
        if (samples.Length < 2)
        {
            return new MessagePeriodStatisticsResult(samples.Length, 0, 0, 0, 0, 0);
        }

        var intervals = new double[samples.Length - 1];
        for (var i = 1; i < samples.Length; i++)
        {
            intervals[i - 1] = (samples[i] - samples[i - 1]) * 1000.0 / timestampFrequency;
        }

        return new MessagePeriodStatisticsResult(
            samples.Length,
            intervals.Length,
            intervals.Average(),
            intervals.Min(),
            intervals.Max(),
            (samples[^1] - samples[0]) * 1000.0 / timestampFrequency);
    }
}
