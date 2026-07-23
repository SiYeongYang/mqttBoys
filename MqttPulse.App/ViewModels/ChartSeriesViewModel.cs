using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using MqttPulse.App.Infrastructure;
using MqttPulse.App.Models;
using MqttPulse.Core;

namespace MqttPulse.App.ViewModels;

public sealed class ChartSeriesViewModel : ObservableObject
{
    private const int HistoryScanLimit = 300;
    private const int PointLimit = 300;
    private readonly Func<string, TopicViewModel?> _topicResolver;
    private readonly Action<ChartSeriesViewModel> _remove;
    private readonly List<ChartPoint> _points = new(PointLimit);
    private TopicViewModel? _sourceTopic;
    private MqttMessageSnapshot? _lastProcessedMessage;
    private IReadOnlyList<ChartPoint> _displayPoints = Array.Empty<ChartPoint>();
    private bool _isPaused;
    private string _statusText = "Waiting";
    private string _latestValueText = string.Empty;

    public ChartSeriesViewModel(
        string topic,
        JsonScalarMetric metric,
        Brush seriesBrush,
        Func<string, TopicViewModel?> topicResolver,
        Action<ChartSeriesViewModel> remove)
    {
        Topic = topic;
        Metric = metric;
        SeriesBrush = seriesBrush;
        _topicResolver = topicResolver;
        _remove = remove;
        TogglePauseCommand = new RelayCommand(TogglePause);
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public string Topic { get; }

    public JsonScalarMetric Metric { get; }

    public string MetricTitle => Metric.DisplayPath.StartsWith("$.", StringComparison.Ordinal)
        ? Metric.DisplayPath[2..]
        : Metric.DisplayPath;

    public bool IsBoolean => Metric.IsBoolean;

    public Brush SeriesBrush { get; }

    public IReadOnlyList<ChartPoint> DisplayPoints
    {
        get => _displayPoints;
        private set => SetProperty(ref _displayPoints, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }
    }

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LatestValueText
    {
        get => _latestValueText;
        private set => SetProperty(ref _latestValueText, value);
    }

    public ICommand TogglePauseCommand { get; }

    public ICommand RemoveCommand { get; }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        StatusText = paused
            ? $"Paused · {_points.Count:N0} points"
            : $"{_points.Count:N0} points";
    }

    public void Refresh()
    {
        var topic = _topicResolver(Topic);
        if (topic?.LastMessage is not { } latest)
        {
            StatusText = "Waiting";
            return;
        }

        if (!ReferenceEquals(topic, _sourceTopic))
        {
            _sourceTopic = topic;
            _lastProcessedMessage = null;
        }

        if (ReferenceEquals(latest, _lastProcessedMessage))
        {
            return;
        }

        var history = topic.HistoryNewestFirst(HistoryScanLimit);
        if (IsPaused)
        {
            _lastProcessedMessage = latest;
            StatusText = $"Paused · {_points.Count:N0} points";
            return;
        }

        IReadOnlyList<MqttMessageSnapshot> messages;
        if (_lastProcessedMessage is null)
        {
            messages = new[] { latest };
        }
        else
        {
            var previousIndex = -1;
            for (var index = 0; index < history.Count; index++)
            {
                if (ReferenceEquals(history[index], _lastProcessedMessage))
                {
                    previousIndex = index;
                    break;
                }
            }

            messages = previousIndex >= 0
                ? history.Take(previousIndex).Reverse().ToArray()
                : history.Reverse().ToArray();
        }

        var changed = false;
        foreach (var message in messages)
        {
            if (!JsonScalarExtractor.TryRead(message.PayloadText, Metric, out var value))
            {
                continue;
            }

            _points.Add(new ChartPoint(message.ReceivedAt, value));
            changed = true;
        }

        _lastProcessedMessage = latest;
        if (!changed)
        {
            StatusText = $"{_points.Count:N0} points";
            return;
        }

        if (_points.Count > PointLimit)
        {
            _points.RemoveRange(0, _points.Count - PointLimit);
        }

        DisplayPoints = _points.ToArray();
        LatestValueText = $"Latest {FormatValue(_points[^1].Value)}";
        StatusText = $"{_points.Count:N0} points";
    }

    private void TogglePause()
    {
        SetPaused(!IsPaused);
    }

    private string FormatValue(double value)
    {
        if (IsBoolean)
        {
            return value >= 0.5 ? "true" : "false";
        }

        var absolute = Math.Abs(value);
        return ((absolute > 0 && absolute < 0.001) || absolute >= 1_000_000)
            ? value.ToString("0.###E+0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
