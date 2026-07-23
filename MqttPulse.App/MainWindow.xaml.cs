using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MqttPulse.App.Controls;
using MqttPulse.App.ViewModels;

namespace MqttPulse.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Point _profileTreeDragStart;
    private bool _isCommittingPeriodTopicSuggestion;
    private bool _isCommittingPublishTopicSuggestion;
    private ChartDashboardWindow? _chartWindow;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closed += MainWindow_Closed;
    }

    private void FolderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo
            || combo.SelectedItem is not string folderPath
            || (!combo.IsDropDownOpen && !combo.IsKeyboardFocusWithin))
        {
            return;
        }

        _viewModel.SelectedProfileFolderPath = folderPath;
    }

    private void TopicTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TopicViewModel topic)
        {
            _viewModel.SelectedTopic = topic;
        }
    }

    private void ToolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OpenChartsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowChartWindow();
    }

    private void ValuePayloadViewer_ChartRequested(
        object? sender,
        JsonChartRequestedEventArgs e)
    {
        if (_viewModel.SelectedTopic?.IsLeafTopic != true)
        {
            return;
        }

        var window = ShowChartWindow();
        window.AddChart(_viewModel.SelectedTopic.FullTopic, e.Metric);
        window.Activate();
    }

    private ChartDashboardWindow ShowChartWindow()
    {
        if (_chartWindow is null)
        {
            _chartWindow = new ChartDashboardWindow(_viewModel)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            _chartWindow.Closed += (_, _) => _chartWindow = null;
        }

        if (!_chartWindow.IsVisible)
        {
            _chartWindow.Show();
        }

        return _chartWindow;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _chartWindow?.Close();
        _viewModel.Dispose();
    }

    private void PublishTopicInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isCommittingPublishTopicSuggestion)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            new Action(() => ShowPublishTopicSuggestions(resetSelection: true)));
    }

    private void PublishTopicInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ShowPublishTopicSuggestions(resetSelection: true);
    }

    private void PublishTopicDropButton_Click(object sender, RoutedEventArgs e)
    {
        PublishTopicInput.Focus();
        PublishTopicInput.CaretIndex = PublishTopicInput.Text.Length;
        PublishTopicInput.SelectionLength = 0;
        ShowPublishTopicSuggestions(resetSelection: true);
    }

    private void PublishTopicInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MovePublishTopicSuggestion(1);
                e.Handled = true;
                break;
            case Key.Up:
                MovePublishTopicSuggestion(-1);
                e.Handled = true;
                break;
            case Key.Enter when PublishTopicPopup.IsOpen
                                && PublishTopicSuggestionList.SelectedItem is string topic:
                CommitPublishTopicSuggestion(topic);
                e.Handled = true;
                break;
            case Key.Escape:
                ClosePublishTopicSuggestions();
                e.Handled = true;
                break;
            case Key.Tab:
                ClosePublishTopicSuggestions();
                break;
        }
    }

    private void PublishTopicSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list
            || ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is not ListBoxItem item
            || item.DataContext is not string topic)
        {
            return;
        }

        CommitPublishTopicSuggestion(topic);
        e.Handled = true;
    }

    private void MovePublishTopicSuggestion(int offset)
    {
        var count = PublishTopicSuggestionList.Items.Count;
        if (count == 0)
        {
            ClosePublishTopicSuggestions();
            return;
        }

        PublishTopicPopup.IsOpen = true;
        var current = PublishTopicSuggestionList.SelectedIndex;
        var next = current < 0
            ? (offset > 0 ? 0 : count - 1)
            : Math.Clamp(current + offset, 0, count - 1);
        PublishTopicSuggestionList.SelectedIndex = next;
        PublishTopicSuggestionList.ScrollIntoView(PublishTopicSuggestionList.SelectedItem);
    }

    private void ShowPublishTopicSuggestions(bool resetSelection)
    {
        if (!PublishTopicInput.IsKeyboardFocusWithin)
        {
            return;
        }

        if (resetSelection)
        {
            PublishTopicSuggestionList.SelectedIndex = -1;
        }

        PublishTopicPopup.IsOpen = _viewModel.PublishTopicSuggestions.Count > 0;
    }

    private void CommitPublishTopicSuggestion(string topic)
    {
        _isCommittingPublishTopicSuggestion = true;
        try
        {
            PublishTopicPopup.IsOpen = false;
            PublishTopicSuggestionList.SelectedIndex = -1;
            _viewModel.PublishTopic = topic;
        }
        finally
        {
            _isCommittingPublishTopicSuggestion = false;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                PublishTopicInput.Focus();
                PublishTopicInput.CaretIndex = PublishTopicInput.Text.Length;
                PublishTopicInput.SelectionLength = 0;
            }));
    }

    private void ClosePublishTopicSuggestions()
    {
        PublishTopicPopup.IsOpen = false;
        PublishTopicSuggestionList.SelectedIndex = -1;
    }

    private void PeriodTopicInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isCommittingPeriodTopicSuggestion)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            new Action(() => ShowPeriodTopicSuggestions(resetSelection: true)));
    }

    private void PeriodTopicInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ShowPeriodTopicSuggestions(resetSelection: true);
    }

    private void PeriodTopicDropButton_Click(object sender, RoutedEventArgs e)
    {
        PeriodTopicInput.Focus();
        PeriodTopicInput.CaretIndex = PeriodTopicInput.Text.Length;
        PeriodTopicInput.SelectionLength = 0;
        ShowPeriodTopicSuggestions(resetSelection: true);
    }

    private void PeriodTopicInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MovePeriodTopicSuggestion(1);
                e.Handled = true;
                break;
            case Key.Up:
                MovePeriodTopicSuggestion(-1);
                e.Handled = true;
                break;
            case Key.Enter when PeriodTopicPopup.IsOpen
                                && PeriodTopicSuggestionList.SelectedItem is string topic:
                CommitPeriodTopicSuggestion(topic);
                e.Handled = true;
                break;
            case Key.Escape:
                ClosePeriodTopicSuggestions();
                e.Handled = true;
                break;
            case Key.Tab:
                ClosePeriodTopicSuggestions();
                break;
        }
    }

    private void PeriodTopicSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list
            || ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is not ListBoxItem item
            || item.DataContext is not string topic)
        {
            return;
        }

        CommitPeriodTopicSuggestion(topic);
        e.Handled = true;
    }

    private void PeriodCheckPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            ClosePeriodTopicSuggestions();
        }
    }

    private void MovePeriodTopicSuggestion(int offset)
    {
        var count = PeriodTopicSuggestionList.Items.Count;
        if (count == 0)
        {
            ClosePeriodTopicSuggestions();
            return;
        }

        PeriodTopicPopup.IsOpen = true;
        var current = PeriodTopicSuggestionList.SelectedIndex;
        var next = current < 0
            ? (offset > 0 ? 0 : count - 1)
            : Math.Clamp(current + offset, 0, count - 1);
        PeriodTopicSuggestionList.SelectedIndex = next;
        PeriodTopicSuggestionList.ScrollIntoView(PeriodTopicSuggestionList.SelectedItem);
    }

    private void ShowPeriodTopicSuggestions(bool resetSelection)
    {
        if (!PeriodTopicInput.IsKeyboardFocusWithin || !PeriodTopicInput.IsEnabled)
        {
            return;
        }

        if (resetSelection)
        {
            PeriodTopicSuggestionList.SelectedIndex = -1;
        }

        PeriodTopicPopup.IsOpen = _viewModel.PeriodCheckTopicSuggestions.Count > 0;
    }

    private void CommitPeriodTopicSuggestion(string topic)
    {
        _isCommittingPeriodTopicSuggestion = true;
        try
        {
            PeriodTopicPopup.IsOpen = false;
            PeriodTopicSuggestionList.SelectedIndex = -1;
            _viewModel.PeriodCheckTopicText = topic;
        }
        finally
        {
            _isCommittingPeriodTopicSuggestion = false;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                PeriodTopicInput.Focus();
                PeriodTopicInput.CaretIndex = PeriodTopicInput.Text.Length;
                PeriodTopicInput.SelectionLength = 0;
            }));
    }

    private void ClosePeriodTopicSuggestions()
    {
        PeriodTopicPopup.IsOpen = false;
        PeriodTopicSuggestionList.SelectedIndex = -1;
    }

    private void ProfileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _profileTreeDragStart = e.GetPosition(ProfileTree);
    }

    private void ProfileTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(ProfileTree);
        if (Math.Abs(current.X - _profileTreeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _profileTreeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var source = FindProfileTreeNode(e.OriginalSource as DependencyObject);
        if (source is null)
        {
            return;
        }

        DragDrop.DoDragDrop(ProfileTree, source, DragDropEffects.Move);
    }

    private void ProfileTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ProfileTreeNodeViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ProfileTree_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ProfileTreeNodeViewModel)))
        {
            return;
        }

        var source = (ProfileTreeNodeViewModel)e.Data.GetData(typeof(ProfileTreeNodeViewModel))!;
        var target = FindProfileTreeNode(e.OriginalSource as DependencyObject);
        if (!ReferenceEquals(source, target))
        {
            _viewModel.MoveProfileNode(source, target);
        }

        e.Handled = true;
    }

    private static ProfileTreeNodeViewModel? FindProfileTreeNode(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TreeViewItem item && item.DataContext is ProfileTreeNodeViewModel node)
            {
                return node;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
    private void ProfileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ProfileTreeNodeViewModel node)
        {
            _viewModel.SelectedProfileNode = node;
        }
    }
}
