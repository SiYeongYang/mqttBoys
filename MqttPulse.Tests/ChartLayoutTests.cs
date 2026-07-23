using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MqttPulse.App;
using MqttPulse.App.Controls;
using MqttPulse.App.Models;
using MqttPulse.App.ViewModels;
using MqttPulse.Core;

namespace MqttPulse.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChartLayoutTests
{
    [TestMethod]
    public void ChartWindowWrapsMultipleCardsWithoutHorizontalOverflow()
    {
        RunInSta(() =>
        {
            using var mainViewModel = new MainViewModel();
            var window = new ChartDashboardWindow(mainViewModel)
            {
                ShowInTaskbar = false,
                Opacity = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10_000,
                Top = -10_000
            };

            try
            {
                window.ViewModel.AddChart(
                    "VTS/EDGE_DATA/P_FA_032",
                    new JsonScalarMetric("/value", "$.value", JsonScalarKind.Number));
                window.ViewModel.AddChart(
                    "VTS/EDGE_DATA/P_FA_032",
                    new JsonScalarMetric("/running", "$.running", JsonScalarKind.Boolean));
                window.Show();

                foreach (var viewport in new[]
                         {
                             new Size(1000, 700),
                             new Size(760, 480)
                         })
                {
                    window.Width = viewport.Width;
                    window.Height = viewport.Height;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                    window.UpdateLayout();

                    var scrollViewer = (ScrollViewer)window.FindName("ChartScrollViewer");
                    var charts = FindVisualChildren<LiveScalarChart>(window).ToArray();
                    Assert.HasCount(2, charts);
                    Assert.AreEqual(0, scrollViewer.ScrollableWidth, 0.5);
                    Assert.IsTrue(charts.All(chart => chart.ActualWidth > 300));
                    Assert.IsTrue(charts.All(chart => chart.ActualHeight > 150));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void LiveChartRendersSeriesPixels()
    {
        RunInSta(() =>
        {
            var chart = new LiveScalarChart
            {
                Width = 430,
                Height = 220,
                SeriesBrush = Brushes.Orange,
                Points = new[]
                {
                    Point(0, 10),
                    Point(1, 20),
                    Point(2, 14),
                    Point(3, 28)
                }
            };
            chart.Measure(new Size(430, 220));
            chart.Arrange(new Rect(0, 0, 430, 220));
            chart.UpdateLayout();

            var bitmap = new RenderTargetBitmap(430, 220, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(chart);
            var pixels = new byte[430 * 220 * 4];
            bitmap.CopyPixels(pixels, 430 * 4, 0);

            var coloredPixels = 0;
            for (var index = 0; index < pixels.Length; index += 4)
            {
                var blue = pixels[index];
                var green = pixels[index + 1];
                var red = pixels[index + 2];
                var alpha = pixels[index + 3];
                if (alpha > 0 && red > 180 && green > 70 && blue < 100)
                {
                    coloredPixels++;
                }
            }

            Assert.IsGreaterThan(40, coloredPixels);
        });
    }

    private static ChartPoint Point(int seconds, double value) =>
        new(
            DateTimeOffset.Parse("2026-07-23T20:00:00+09:00").AddSeconds(seconds),
            value);

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }
}
