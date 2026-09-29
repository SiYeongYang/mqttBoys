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
                $"factory/line/device-{i % 500:D3}",
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

    [TestMethod]
    public void TopicTreeVisualNotificationsAreCoalescedWithoutLosingCounts()
    {
        using var viewModel = new MainViewModel();
        var receive = GetPrivateMethod("OnMessageReceived");
        var drain = GetPrivateMethod("DrainPendingMessages");
        var receivedAt = DateTimeOffset.Parse("2026-07-23T19:30:00+09:00");

        receive.Invoke(
            viewModel,
            new object[]
            {
                new MqttMessageSnapshot(
                    "factory/line/device",
                    "{\"value\":1}",
                    receivedAt,
                    Qos: 0,
                    Retain: false,
                    ReceivedStopwatchTimestamp: 1)
            });
        drain.Invoke(viewModel, null);

        var brokerRoot = viewModel.RootTopics.Single();
        var detailNotifications = 0;
        brokerRoot.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TopicViewModel.DetailText))
            {
                detailNotifications++;
            }
        };

        receive.Invoke(
            viewModel,
            new object[]
            {
                new MqttMessageSnapshot(
                    "factory/line/device",
                    "{\"value\":2}",
                    receivedAt.AddMilliseconds(1),
                    Qos: 0,
                    Retain: false,
                    ReceivedStopwatchTimestamp: 2)
            });
        drain.Invoke(viewModel, null);

        Assert.AreEqual(2, brokerRoot.MessageCount);
        Assert.AreEqual(0, detailNotifications);

        GetPrivateField("_lastTopicVisualRefreshTimestamp").SetValue(viewModel, 0L);
        drain.Invoke(viewModel, null);

        Assert.AreEqual(1, detailNotifications);
    }

    [TestMethod]
    public void LargeBurstKeepsTheQueueBoundedAndTheSelectedPreviewSmall()
    {
        using var viewModel = new MainViewModel();
        var receive = GetPrivateMethod("OnMessageReceived");
        var drain = GetPrivateMethod("DrainPendingMessages");
        var payload = "{\"data\":\"" + new string('x', 400_000) + "\"}";
        var receivedAt = DateTimeOffset.Parse("2026-09-30T12:00:00+09:00");

        for (var i = 0; i < 100; i++)
        {
            receive.Invoke(viewModel, new object[]
            {
                new MqttMessageSnapshot("factory/large", payload, receivedAt.AddMilliseconds(i), 0, false)
            });
        }

        Assert.IsLessThan(100, viewModel.PendingCount);
        var stopwatch = Stopwatch.StartNew();
        drain.Invoke(viewModel, null);
        stopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(500), stopwatch.Elapsed);
        var topic = viewModel.FindLeafTopic("factory/large");
        Assert.IsNotNull(topic);

        stopwatch.Restart();
        viewModel.SelectedTopic = topic;
        stopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
        Assert.IsLessThan(17_000, viewModel.ValuePayloadText.Length);
        Assert.HasCount(5, viewModel.SelectedTopicHistory);
    }

    [TestMethod]
    public void ClearingPendingMessagesCompletesDuringContinuousReceive()
    {
        using var viewModel = new MainViewModel();
        var receive = GetPrivateMethod("OnMessageReceived");
        var message = new MqttMessageSnapshot("factory/large", new string('x', 100_000),
            DateTimeOffset.UtcNow, 0, false);
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var producer = Task.Run(() =>
        {
            started.Set();
            while (!cancellation.IsCancellationRequested)
            {
                receive.Invoke(viewModel, new object[] { message });
            }
        });

        started.Wait();
        var clear = Task.Run(() => viewModel.ClearTopicsCommand.Execute(null));
        var finishedWhileReceiving = clear.Wait(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        producer.Wait();
        clear.Wait();
        Assert.IsTrue(finishedWhileReceiving, "Clear must stop at the queue size observed when it started.");
    }

    private static MethodInfo GetPrivateMethod(string name)
    {
        return typeof(MainViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new MissingMethodException(nameof(MainViewModel), name);
    }

    private static FieldInfo GetPrivateField(string name)
    {
        return typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new MissingFieldException(nameof(MainViewModel), name);
    }
}
