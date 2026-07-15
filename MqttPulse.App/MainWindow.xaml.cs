using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MqttPulse.App.ViewModels;

namespace MqttPulse.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Point _profileTreeDragStart;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
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

    private void PeriodTopicCombo_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox combo
            || e.Key is Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Tab)
        {
            return;
        }

        combo.IsDropDownOpen = _viewModel.PeriodCheckTopicSuggestions.Count > 0;
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
