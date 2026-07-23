using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MqttPulse.App;
using MqttPulse.App.Models;
using MqttPulse.App.ViewModels;

namespace MqttPulse.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MainLayoutTests
{
    [TestMethod]
    public void DefaultLayoutKeepsTopicsMuchNarrowerThanDetailAtDesktopAndLaptopWidths()
    {
        RunInWindow(window =>
        {
            var content = (Grid)window.FindName("MainContentGrid");
            var header = (Border)window.FindName("MainHeader");

            Assert.AreEqual(46, header.ActualHeight, 0.5);
            foreach (var viewport in new[]
                     {
                         new Size(1920, 1080),
                         new Size(1440, 900),
                         new Size(1366, 768),
                         new Size(1100, 720)
                     })
            {
                window.Width = viewport.Width;
                window.Height = viewport.Height;
                window.UpdateLayout();

                Assert.IsGreaterThan(content.ColumnDefinitions[0].ActualWidth * 2, content.ColumnDefinitions[2].ActualWidth);
                Assert.IsLessThanOrEqualTo(
                    content.ActualWidth + 1,
                    content.ColumnDefinitions.Sum(x => x.ActualWidth));
                Assert.IsLessThanOrEqualTo(
                    header.ActualWidth + 1,
                    ((FrameworkElement)header.Child).DesiredSize.Width);
                var detail = (Grid)window.FindName("MainDetailGrid");
                Assert.IsLessThanOrEqualTo(
                    detail.ActualHeight + 1,
                    detail.RowDefinitions.Sum(x => x.ActualHeight));
            }
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

            Assert.AreEqual(7, verticalScrollBar.Width, 0.1);
            Assert.AreEqual(7, horizontalScrollBar.Height, 0.1);

            var splitter = new GridSplitter
            {
                Style = (Style)window.FindResource("VerticalSplitterStyle")
            };
            splitter.ApplyTemplate();
            var line = (Border)splitter.Template.FindName("SplitterLine", splitter);

            Assert.AreEqual(1, line.Width, 0.1);
        });
    }

    [TestMethod]
    public void HeaderOrderAndPausePlacementMatchTheInspectionWorkflow()
    {
        RunInWindow(window =>
        {
            var header = (Border)window.FindName("MainHeader");
            var caption = (TextBlock)window.FindName("ConnectedBrokerCaption");
            var connection = (Button)window.FindName("ConnectionToggleButton");
            var search = (TextBox)window.FindName("HeaderSearchInput");
            var valuePanel = (Grid)window.FindName("ValuePanel");
            var pause = (Button)window.FindName("ValuePauseButton");
            var detail = (Grid)window.FindName("MainDetailGrid");
            var publishPayload = (TextBox)window.FindName("PublishPayloadInput");

            Assert.AreEqual(3, Grid.GetColumn(caption));
            Assert.AreEqual(4, Grid.GetColumn(connection));
            Assert.AreEqual(5, Grid.GetColumn(search));
            Assert.IsTrue(pause.IsDescendantOf(valuePanel));
            Assert.IsFalse(pause.IsDescendantOf(header));
            Assert.AreEqual(170, detail.RowDefinitions[2].ActualHeight, 0.5);
            Assert.IsGreaterThan(100, publishPayload.ActualHeight);
        });
    }

    [TestMethod]
    public void ConnectionTreeUsesDifferentFolderAndBrokerIcons()
    {
        RunInWindow(window =>
        {
            var template = (HierarchicalDataTemplate)window.FindResource(
                new DataTemplateKey(typeof(ProfileTreeNodeViewModel)));
            var folder = new ProfileTreeNodeViewModel("Factory", "Factory", profile: null);
            var broker = new ProfileTreeNodeViewModel(
                "Edge broker",
                "Factory",
                new BrokerProfile { Host = "172.16.1.224", Port = 1883 });

            var folderPresenter = RealizeTemplate(template, folder);
            var brokerPresenter = RealizeTemplate(template, broker);
            var folderIcon = (FrameworkElement)template.FindName("FolderIcon", folderPresenter);
            var folderBrokerIcon = (FrameworkElement)template.FindName("BrokerIcon", folderPresenter);
            var brokerFolderIcon = (FrameworkElement)template.FindName("FolderIcon", brokerPresenter);
            var brokerIcon = (FrameworkElement)template.FindName("BrokerIcon", brokerPresenter);

            Assert.AreEqual(Visibility.Visible, folderIcon.Visibility);
            Assert.AreEqual(Visibility.Collapsed, folderBrokerIcon.Visibility);
            Assert.AreEqual(Visibility.Collapsed, brokerFolderIcon.Visibility);
            Assert.AreEqual(Visibility.Visible, brokerIcon.Visibility);
        });
    }

    private static ContentPresenter RealizeTemplate(
        HierarchicalDataTemplate template,
        ProfileTreeNodeViewModel node)
    {
        var presenter = new ContentPresenter
        {
            Content = node,
            ContentTemplate = template
        };
        presenter.Measure(new Size(280, 80));
        presenter.Arrange(new Rect(0, 0, 280, 80));
        presenter.UpdateLayout();
        return presenter;
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
