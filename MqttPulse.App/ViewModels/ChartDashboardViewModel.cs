using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MqttPulse.App.Infrastructure;
using MqttPulse.Core;

namespace MqttPulse.App.ViewModels;

public sealed class ChartDashboardViewModel : ObservableObject, IDisposable
{
    private static readonly Brush[] SeriesPalette =
    {
        Frozen("#F59E0B"),
        Frozen("#176E63"),
        Frozen("#2563A8"),
        Frozen("#B13B6C"),
        Frozen("#6D5AA8"),
        Frozen("#C05B24")
    };

    private readonly MainViewModel _mainViewModel;
    private readonly DispatcherTimer _refreshTimer;
    private int _nextColorIndex;

    public ChartDashboardViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        PauseAllCommand = new RelayCommand(() => SetAllPaused(true), () => Charts.Count > 0);
        ResumeAllCommand = new RelayCommand(() => SetAllPaused(false), () => Charts.Count > 0);
        ClearAllCommand = new RelayCommand(ClearAll, () => Charts.Count > 0);
        Charts.CollectionChanged += Charts_CollectionChanged;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
    }

    public ObservableCollection<ChartSeriesViewModel> Charts { get; } = new();

    public RelayCommand PauseAllCommand { get; }

    public RelayCommand ResumeAllCommand { get; }

    public RelayCommand ClearAllCommand { get; }

    public string ChartCountText => $"{Charts.Count:N0} charts";

    public Visibility EmptyVisibility => Charts.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public ChartSeriesViewModel AddChart(string topic, JsonScalarMetric metric)
    {
        var existing = Charts.FirstOrDefault(chart =>
            chart.Topic.Equals(topic, StringComparison.Ordinal)
            && chart.Metric.Pointer.Equals(metric.Pointer, StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.SetPaused(false);
            existing.Refresh();
            return existing;
        }

        var chart = new ChartSeriesViewModel(
            topic,
            metric,
            SeriesPalette[_nextColorIndex++ % SeriesPalette.Length],
            _mainViewModel.FindLeafTopic,
            RemoveChart);
        Charts.Add(chart);
        chart.Refresh();
        return chart;
    }

    public void RefreshNow()
    {
        foreach (var chart in Charts)
        {
            chart.Refresh();
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        Charts.CollectionChanged -= Charts_CollectionChanged;
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshNow();
    }

    private void RemoveChart(ChartSeriesViewModel chart)
    {
        Charts.Remove(chart);
    }

    private void SetAllPaused(bool paused)
    {
        foreach (var chart in Charts)
        {
            chart.SetPaused(paused);
        }
    }

    private void ClearAll()
    {
        Charts.Clear();
    }

    private void Charts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ChartCountText));
        OnPropertyChanged(nameof(EmptyVisibility));
        PauseAllCommand.RaiseCanExecuteChanged();
        ResumeAllCommand.RaiseCanExecuteChanged();
        ClearAllCommand.RaiseCanExecuteChanged();
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
