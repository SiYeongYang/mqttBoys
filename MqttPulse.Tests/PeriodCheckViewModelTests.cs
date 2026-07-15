using System.IO;
using MqttPulse.App.Services;
using MqttPulse.App.ViewModels;

namespace MqttPulse.Tests;

[TestClass]
public sealed class PeriodCheckViewModelTests
{
    [TestMethod]
    public void SelectingPeriodCheckHistoryShowsItsDetailedResult()
    {
        using var viewModel = new MainViewModel();
        var item = new PeriodCheckHistoryItemViewModel(
            new DateTime(2026, 7, 15, 14, 30, 0),
            "Edge/data/device",
            "Avg 10 ms",
            "Topic: Edge/data/device\r\nAverage: 10 ms");

        viewModel.PeriodCheckHistory.Add(item);
        viewModel.SelectedPeriodCheckHistoryItem = item;

        Assert.AreEqual(item.ResultText, viewModel.PeriodCheckResultText);
        Assert.AreEqual("2026-07-15 14:30:00", item.CompletedAtText);
    }

    [TestMethod]
    public void PeriodCheckHistoryIsNotPersistedAcrossViewModels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mqttpulse-period-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ProfileStore(path);
            using (var first = new MainViewModel(store))
            {
                first.PeriodCheckHistory.Add(new PeriodCheckHistoryItemViewModel(
                    DateTime.Now,
                    "Edge/data/device",
                    "Avg 10 ms",
                    "Average: 10 ms"));
            }

            using var second = new MainViewModel(store);
            Assert.IsEmpty(second.PeriodCheckHistory);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
