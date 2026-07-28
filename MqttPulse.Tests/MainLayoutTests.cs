using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MqttPulse.App;
using MqttPulse.App.Controls;
using MqttPulse.App.Models;
using MqttPulse.App.ViewModels;
using MqttPulse.Core;

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
            const string topic = "factory/line/device-030";

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
            var chart = (Button)window.FindName("ValueChartButton");
            var valueViewer = (JsonPayloadViewer)window.FindName("ValuePayloadViewer");
            var formatter = (Grid)window.FindName("JsonFormatterPanel");
            var detail = (Grid)window.FindName("MainDetailGrid");
            var publishPayload = (TextBox)window.FindName("PublishPayloadInput");

            Assert.AreEqual(3, Grid.GetColumn(search));
            Assert.AreEqual(5, Grid.GetColumn(caption));
            Assert.AreEqual(6, Grid.GetColumn(connection));
            Assert.IsTrue(chart.IsDescendantOf(valuePanel));
            Assert.IsTrue(pause.IsDescendantOf(valuePanel));
            Assert.IsFalse(pause.IsDescendantOf(header));
            Assert.IsTrue(valueViewer.EnableChartActions);
            Assert.AreEqual(Visibility.Collapsed, formatter.Visibility);
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
                new BrokerProfile { Host = "192.0.2.10", Port = 1883 });

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

    [TestMethod]
    public void ConnectionFolderEditorUsesFooterSaveAndOneLeftDeleteAction()
    {
        RunInWindow(window =>
        {
            var folderName = (TextBox)window.FindName("FolderNameInput");
            var deleteFolder = (Button)window.FindName("DeleteFolderButton");
            var buttons = FindLogicalDescendants<Button>(window).ToArray();

            Assert.IsNotNull(folderName);
            Assert.IsNotNull(deleteFolder);
            Assert.HasCount(
                1,
                buttons.Where(button => Equals(button.Content, "Delete folder")).ToArray());
            Assert.IsEmpty(
                buttons.Where(button => Equals(button.Content, "Rename folder")).ToArray());
            Assert.HasCount(
                1,
                buttons.Where(button => Equals(button.Content, "Save changes")).ToArray());
        });
    }

    [TestMethod]
    public void ConnectionTreeShowsBeforeInsideAndAfterDropHints()
    {
        RunInWindow(window =>
        {
            var template = (HierarchicalDataTemplate)window.FindResource(
                new DataTemplateKey(typeof(ProfileTreeNodeViewModel)));
            var folder = new ProfileTreeNodeViewModel("Factory", "Factory", profile: null);
            var presenter = RealizeTemplate(template, folder);
            var surface = (Border)template.FindName("ProfileNodeDropSurface", presenter);

            folder.DropPosition = ProfileNodeDropPosition.Before;
            presenter.UpdateLayout();
            Assert.AreEqual(new Thickness(0, 2, 0, 0), surface.BorderThickness);

            folder.DropPosition = ProfileNodeDropPosition.Into;
            presenter.UpdateLayout();
            Assert.AreEqual(new Thickness(1), surface.BorderThickness);
            Assert.AreNotEqual(Brushes.Transparent, surface.Background);

            folder.DropPosition = ProfileNodeDropPosition.After;
            presenter.UpdateLayout();
            Assert.AreEqual(new Thickness(0, 0, 0, 2), surface.BorderThickness);
        });
    }

    [TestMethod]
    public void ValueAndSelectedViewersExposeChartActionsBesideNumericAndBooleanRows()
    {
        RunInWindow(window =>
        {
            foreach (var name in new[] { "ValuePayloadViewer", "SelectedPayloadViewer" })
            {
                var viewer = (JsonPayloadViewer)window.FindName(name);
                viewer.Text = "{\"value\":42,\"running\":true,\"label\":\"line\"}";
                viewer.UpdateLayout();

                var paragraph = viewer.Document.Blocks.OfType<Paragraph>().Single();
                var actions = paragraph.Inlines.OfType<Hyperlink>().ToArray();
                var metrics = actions.Select(action => (JsonScalarMetric)action.Tag).ToArray();

                Assert.IsTrue(viewer.EnableChartActions);
                Assert.HasCount(2, actions);
                CollectionAssert.AreEquivalent(
                    new[] { "$.value", "$.running" },
                    metrics.Select(metric => metric.DisplayPath).ToArray());
            }
        });
    }

    [TestMethod]
    public void SelectedViewerFindHighlightsAndNavigatesCaseInsensitiveMatches()
    {
        RunInWindow(window =>
        {
            var viewer = (JsonPayloadViewer)window.FindName("SelectedPayloadViewer");
            viewer.Text = """
                          {
                            "first": "alpha",
                            "second": "ALPHA",
                            "third": "alpha"
                          }
                          """;

            viewer.SetSearchQuery("alpha");
            window.UpdateLayout();

            Assert.AreEqual(3, viewer.SearchMatchCount);
            Assert.AreEqual(1, viewer.ActiveSearchMatchNumber);
            Assert.AreEqual("1 / 3", viewer.SearchResultText);
            var highlighted = viewer.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
                .Where(run => run.Background is not null)
                .ToArray();
            Assert.IsGreaterThanOrEqualTo(3, highlighted.Length);
            Assert.AreEqual(2, highlighted.Select(run => run.Background).Distinct().Count());

            viewer.MoveSearchMatch(1);
            Assert.AreEqual(2, viewer.ActiveSearchMatchNumber);
            Assert.AreEqual("2 / 3", viewer.SearchResultText);

            viewer.MoveSearchMatch(-2);
            Assert.AreEqual(3, viewer.ActiveSearchMatchNumber);

            viewer.SetSearchQuery("\"first\": \"alpha\"");
            Assert.AreEqual(1, viewer.SearchMatchCount);
            Assert.IsGreaterThan(
                1,
                viewer.Document.Blocks
                    .OfType<Paragraph>()
                    .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
                    .Count(run => run.Background is not null));

            viewer.ClearSearch();
            Assert.AreEqual(0, viewer.SearchMatchCount);
            Assert.IsTrue(
                viewer.Document.Blocks
                    .OfType<Paragraph>()
                    .SelectMany(paragraph => paragraph.Inlines.OfType<Run>())
                    .All(run => run.Background is null));
        });
    }

    [TestMethod]
    public void SelectedFindCommandOpensCompactPanelAndSupportsResultControls()
    {
        RunInWindow(window =>
        {
            var viewer = (JsonPayloadViewer)window.FindName("SelectedPayloadViewer");
            var panel = (Border)window.FindName("SelectedSearchPanel");
            var content = (Grid)window.FindName("SelectedContentGrid");
            var input = (TextBox)window.FindName("SelectedSearchInput");
            var result = (TextBlock)window.FindName("SelectedSearchResultText");
            var next = (Button)window.FindName("SelectedSearchNextButton");
            var close = (Button)window.FindName("SelectedSearchCloseButton");
            var searchButton = (Button)window.FindName("SelectedSearchButton");
            var binding = window.InputBindings.OfType<KeyBinding>().Single(x =>
                x.Key == Key.F && x.Modifiers == ModifierKeys.Control);

            Assert.AreSame(ApplicationCommands.Find, binding.Command);
            Assert.AreSame(ApplicationCommands.Find, searchButton.Command);
            Assert.AreEqual(Visibility.Collapsed, panel.Visibility);

            viewer.Text = "{\"a\":\"alpha\",\"b\":\"ALPHA\",\"c\":\"alpha\"}";
            ApplicationCommands.Find.Execute(null, window);
            PumpDispatcher(TimeSpan.FromMilliseconds(30));
            Assert.AreEqual(Visibility.Visible, panel.Visibility);

            input.Text = "alpha";
            PumpDispatcher(TimeSpan.FromMilliseconds(220));
            Assert.AreEqual("1 / 3", result.Text);
            Assert.IsTrue(next.IsEnabled);

            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual("2 / 3", result.Text);

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
                Assert.IsLessThanOrEqualTo(content.ActualWidth, panel.ActualWidth);
                Assert.IsLessThanOrEqualTo(content.ActualHeight, panel.ActualHeight);
            }

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(Visibility.Collapsed, panel.Visibility);
            Assert.AreEqual(string.Empty, viewer.SearchQuery);
        });
    }

    [TestMethod]
    public void ValueModeButtonsSwitchBetweenRawAndDiffViewers()
    {
        RunInWindow(window =>
        {
            var viewModel = (MainViewModel)window.DataContext;
            var raw = (JsonPayloadViewer)window.FindName("ValuePayloadViewer");
            var diff = (JsonDiffViewer)window.FindName("ValueDiffViewer");

            Assert.IsTrue(viewModel.IsValueRawMode);
            Assert.AreEqual(Visibility.Visible, raw.Visibility);
            Assert.AreEqual(Visibility.Collapsed, diff.Visibility);

            viewModel.ShowValueDiffCommand.Execute(null);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            Assert.AreEqual(Visibility.Collapsed, raw.Visibility);
            Assert.AreEqual(Visibility.Visible, diff.Visibility);
            Assert.IsTrue(diff.IsActive);
        });
    }

    [TestMethod]
    public void DiffViewerRendersAddedRemovedLinesAndComparisonSummary()
    {
        RunInWindow(window =>
        {
            var viewModel = (MainViewModel)window.DataContext;
            var diff = (JsonDiffViewer)window.FindName("ValueDiffViewer");
            diff.BaselineText = "{\n  \"value\": 1\n}";
            diff.CurrentText = "{\n  \"value\": 2\n}";
            viewModel.ShowValueDiffCommand.Execute(null);

            PumpDispatcher(TimeSpan.FromMilliseconds(250));

            var paragraphs = diff.Document.Blocks.OfType<Paragraph>().ToArray();
            var text = new TextRange(diff.Document.ContentStart, diff.Document.ContentEnd).Text;
            Assert.IsTrue(paragraphs.Any(paragraph => paragraph.Background != Brushes.Transparent));
            StringAssert.Contains(text, "-   \"value\": 1");
            StringAssert.Contains(text, "+   \"value\": 2");
            StringAssert.Contains(text, "Comparing with previous message: + 1 lines, - 1 lines");
        });
    }

    [TestMethod]
    public void DiffModeUsesPreviousMessageAndDoesNotDependOnSelectedHistory()
    {
        RunInWindow(window =>
        {
            var viewModel = (MainViewModel)window.DataContext;
            var topic = new TopicViewModel("device", "factory/line/device", historyCapacity: 10);
            var oldest = Message("{\"value\":1}", "2026-07-27T12:00:00+09:00");
            var previous = Message("{\"value\":2}", "2026-07-27T12:00:01+09:00");
            var latest = Message("{\"value\":3}", "2026-07-27T12:00:02+09:00");
            topic.Record(oldest, isLeaf: true, leafTopicWasNew: true);
            topic.Record(previous, isLeaf: true, leafTopicWasNew: false);
            topic.Record(latest, isLeaf: true, leafTopicWasNew: false);

            viewModel.SelectedTopic = topic;
            viewModel.SelectedHistoryItem = viewModel.SelectedTopicHistory.Single(
                item => ReferenceEquals(item.Message, oldest));
            viewModel.ShowValueDiffCommand.Execute(null);
            PumpDispatcher(TimeSpan.FromMilliseconds(350));

            var diff = (JsonDiffViewer)window.FindName("ValueDiffViewer");
            StringAssert.Contains(diff.BaselineText, "\"value\": 2");
            StringAssert.Contains(diff.CurrentText, "\"value\": 3");
            StringAssert.Contains(viewModel.SelectedPayloadText, "\"value\": 1");
            Assert.IsFalse(diff.BaselineText.Contains("\"value\": 1", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void JsonFormatterFitsTheMinimumWindow()
    {
        RunInWindow(window =>
        {
            window.Width = 1100;
            window.Height = 720;
            var viewModel = (MainViewModel)window.DataContext;
            viewModel.JsonFormatterInput = "{\"array\":[1,2],\"active\":true}";
            viewModel.OpenJsonFormatterCommand.Execute(null);
            viewModel.FormatJsonFormatterCommand.Execute(null);
            window.UpdateLayout();

            var dialog = (Grid)window.FindName("JsonFormatterDialog");
            var structure = (TreeView)window.FindName("JsonStructureTree");

            Assert.AreEqual(Visibility.Visible, viewModel.JsonFormatterVisibility);
            Assert.HasCount(1, structure.Items);
            Assert.IsLessThanOrEqualTo(window.ActualWidth, dialog.ActualWidth);
            Assert.IsLessThanOrEqualTo(window.ActualHeight, dialog.ActualHeight);
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

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindLogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static MqttMessageSnapshot Message(string payload, string receivedAt)
    {
        return new MqttMessageSnapshot(
            "factory/line/device",
            payload,
            DateTimeOffset.Parse(receivedAt),
            Qos: 0,
            Retain: false);
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
