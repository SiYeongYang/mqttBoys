using System.Collections.ObjectModel;
using MqttPulse.App.Infrastructure;
using MqttPulse.Core;

namespace MqttPulse.App.ViewModels;

public sealed class TopicViewModel : ObservableObject
{
    private readonly Dictionary<string, TopicViewModel> _childrenByName = new(StringComparer.Ordinal);
    private readonly BoundedMessageHistory _history;
    private bool _isExpanded;
    private int _leafTopicCount;
    private int _messageCount;
    private string _lastPayloadPreview = string.Empty;

    public TopicViewModel(string name, string fullTopic, int historyCapacity)
    {
        Name = name;
        FullTopic = fullTopic;
        _history = new BoundedMessageHistory(historyCapacity);
    }

    public string Name { get; }

    public string FullTopic { get; }

    public ObservableCollection<TopicViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public int LeafTopicCount
    {
        get => _leafTopicCount;
        private set => SetProperty(ref _leafTopicCount, value);
    }

    public int MessageCount
    {
        get => _messageCount;
        private set => SetProperty(ref _messageCount, value);
    }

    public string LastPayloadPreview
    {
        get => _lastPayloadPreview;
        private set => SetProperty(ref _lastPayloadPreview, value);
    }

    public bool IsLeafTopic { get; private set; }

    public MqttMessageSnapshot? LastMessage { get; private set; }

    public string DisplayName => Name;

    public string DetailText => IsLeafTopic
        ? $"= {LastPayloadPreview}"
        : $"({LeafTopicCount:N0} {Pluralize("topic", LeafTopicCount)}, {MessageCount:N0} {Pluralize("message", MessageCount)})";

    public TopicViewModel GetOrCreateChild(string name, string fullTopic, int historyCapacity)
    {
        if (_childrenByName.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var created = new TopicViewModel(name, fullTopic, historyCapacity);
        _childrenByName.Add(name, created);
        Children.Add(created);
        return created;
    }

    public void Record(MqttMessageSnapshot message, bool isLeaf, bool leafTopicWasNew)
    {
        MessageCount++;
        LastMessage = message;
        if (leafTopicWasNew)
        {
            LeafTopicCount++;
        }

        if (isLeaf)
        {
            IsLeafTopic = true;
            LastPayloadPreview = PayloadFormatter.BuildPreview(message.PayloadText, previewLimit: 120);
            _history.Add(message);
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DetailText));
    }

    public IReadOnlyList<MqttMessageSnapshot> HistoryNewestFirst()
    {
        return _history.NewestFirst();
    }

    public IReadOnlyList<MqttMessageSnapshot> HistoryNewestFirst(int maxCount)
    {
        return _history.NewestFirst(maxCount);
    }

    private static string Pluralize(string word, int count) => count == 1 ? word : word + "s";
}