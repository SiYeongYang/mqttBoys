using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MqttPulse.App;
using MqttPulse.App.ViewModels;

namespace MqttPulse.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MainLayoutTests
{
    [TestMethod]
    public void DefaultLayoutKeepsTopicsMuchWiderThanDetailAtDesktopAndLaptopWidths()
    {
        RunInWindow(window =>
        {
            var content = (Grid)window.FindName("MainContentGrid");
            var header = (Border)window.FindName("MainHeader");

            Assert.AreEqual(46, header.ActualHeight, 0.5);
            Assert.IsGreaterThan(content.ColumnDefinitions[2].ActualWidth * 2, content.ColumnDefinitions[0].ActualWidth);

            window.Width = 1100;
            window.Height = 720;
            window.UpdateLayout();

            Assert.IsGreaterThan(0, content.ColumnDefinitions[2].ActualWidth);
            Assert.IsGreaterThan(content.ColumnDefinitions[2].ActualWidth, content.ColumnDefinitions[0].ActualWidth);
            Assert.IsLessThanOrEqualTo(
                content.ActualWidth + 1,
                content.ColumnDefinitions.Sum(x => x.ActualWidth));
        });
    }

    [TestMethod]
    public void PublishTopicSuggestionCommitsWithoutSelectingTheInputText()
    {
        RunInWindow(window =>
        {
            var viewModel = (MainViewModel)window.DataContext;
            var input = (TextBox)window.FindName("PublishTopicInput");
            var popup = (Popup)window.FindName("PublishTopicPopup");
            var list = (ListBox)window.FindName("PublishTopicSuggestionList");
            const string topic = "VTS/EDGE_DATA/P_FA_030";

            viewModel.PublishTopicSuggestions.Add(topic);
            popup.IsOpen = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.HasCount(1, list.Items);

            var commit = typeof(MainWindow).GetMethod(
                "CommitPublishTopicSuggestion",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CommitPublishTopicSuggestion");
            commit.Invoke(window, new object[] { topic });
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.AreEqual(topic, viewModel.PublishTopic);
            Assert.AreEqual(topic, input.Text);
            Assert.AreEqual(topic.Length, input.CaretIndex);
            Assert.AreEqual(0, input.SelectionLength);
            Assert.IsFalse(popup.IsOpen);
        });
    }

    [TestMethod]
    public void ScrollBarsStayThinAndTheSplitterRendersAsASingleLine()
    {
        RunInWindow(window =>
        {
            var scrollStyle = (Style)window.FindResource(typeof(ScrollBar));
            var verticalScrollBar = new ScrollBar
            {
                Orientation = Orientation.Vertical,
                Style = scrollStyle
            };
            var horizontalScrollBar = new ScrollBar
            {
                Orientation = Orientation.Horizontal,
                Style = scrollStyle
            };

            verticalScrollBar.ApplyTemplate();
            horizontalScrollBar.ApplyTemplate();

            Assert.AreEqual(10, verticalScrollBar.Width, 0.1);
            Assert.AreEqual(10, horizontalScrollBar.Height, 0.1);

            var splitter = new GridSplitter
            {
                Style = (Style)window.FindResource("VerticalSplitterStyle")
            };
            splitter.ApplyTemplate();
            var line = (Border)splitter.Template.FindName("SplitterLine", splitter);

            Assert.AreEqual(1, line.Width, 0.1);
        });
    }

    private static void RunInWindow(Action<MainWindow> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow
                {
                    Width = 1440,
                    Height = 900,
                    Opacity = 0,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10_000,
                    Top = -10_000
                };

                try
                {
                    window.Show();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                    window.UpdateLayout();
                    test(window);
                }
                finally
                {
                    window.Close();
                }
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
