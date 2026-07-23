using System.Diagnostics;
using System.Reflection;
using MqttPulse.App.ViewModels;
using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
public sealed class LiveUpdatePerformanceTests
{
    [TestMethod]
    public void BurstTrafficIsDrainedInShortUiBatches()
    {
        using var viewModel = new MainViewModel();
        var receive = GetPrivateMethod("OnMessageReceived");
        var drain = GetPrivateMethod("DrainPendingMessages");
        var receivedAt = DateTimeOffset.Parse("2026-07-23T19:30:00+09:00");

        for (var i = 0; i < 10_000; i++)
        {
            var message = new MqttMessageSnapshot(
                $"VTS/EDGE_DATA/device-{i % 500:D3}",
                $"{{\"sequence\":{i},\"value\":\"{new string('x', 48)}\"}}",
                receivedAt,
                Qos: 0,
                Retain: false,
                ReceivedStopwatchTimestamp: i + 1);
            receive.Invoke(viewModel, new object[] { message });
        }

        var stopwatch = Stopwatch.StartNew();
        drain.Invoke(viewModel, null);
        stopwatch.Stop();

        Assert.IsGreaterThan(0L, viewModel.ReceivedMessages);
        Assert.IsLessThan(10_000L, viewModel.ReceivedMessages);
        Assert.IsGreaterThan(0, viewModel.PendingCount);
        Assert.IsLessThan(
            TimeSpan.FromMilliseconds(500),
            stopwatch.Elapsed,
            "A single UI drain must yield instead of monopolizing the dispatcher.");
    }

    private static MethodInfo GetPrivateMethod(string name)
    {
        return typeof(MainViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new MissingMethodException(nameof(MainViewModel), name);
    }
}
