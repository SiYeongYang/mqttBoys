using System.Collections.ObjectModel;
using MqttPulse.App.Infrastructure;
using MqttPulse.Core;

namespace MqttPulse.App.ViewModels;

public sealed class TopicViewModel : ObservableObject
{
    private readonly Dictionary<string, TopicViewModel> _childrenByName = new(StringComparer.Ordinal);
    private readonly BoundedMessageHistory _history;
    private bool _isExpanded;
    private bool _isSearchVisible = true;
    private bool _searchStateCaptured;
    private bool _expandedBeforeSearch;
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

    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        private set => SetProperty(ref _isSearchVisible, value);
    }

    public int LeafTopicCount => _leafTopicCount;

    public int MessageCount => _messageCount;

    public string LastPayloadPreview => _lastPayloadPreview;

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

    public void Record(MqttMessageSnapshot message, bool isLeaf, bool leafTopicWasNew, bool notify = true)
    {
        _messageCount++;
        LastMessage = message;
        if (leafTopicWasNew)
        {
            _leafTopicCount++;
        }

        if (isLeaf)
        {
            IsLeafTopic = true;
            _history.Add(message);
        }

        if (notify)
        {
            NotifyRecordChanged();
        }
    }

    public void NotifyRecordChanged()
    {
        if (IsLeafTopic && LastMessage is not null)
        {
            _lastPayloadPreview = PayloadFormatter.BuildPreview(LastMessage.PayloadText, previewLimit: 120);
        }

        OnPropertyChanged(nameof(LeafTopicCount));
        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(LastPayloadPreview));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DetailText));
    }

    public bool ApplySearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RestoreSearchState();
            return true;
        }

        if (!_searchStateCaptured)
        {
            _expandedBeforeSearch = IsExpanded;
            _searchStateCaptured = true;
        }

        var normalizedQuery = query.Trim();
        var isDirectMatch = Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                            || FullTopic.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
        var hasVisibleChild = false;
        foreach (var child in Children)
        {
            hasVisibleChild |= child.ApplySearch(normalizedQuery);
        }

        IsSearchVisible = isDirectMatch || hasVisibleChild;
        if (hasVisibleChild)
        {
            IsExpanded = true;
        }

        return IsSearchVisible;
    }

    public IReadOnlyList<MqttMessageSnapshot> HistoryNewestFirst()
    {
        return _history.NewestFirst();
    }

    public IReadOnlyList<MqttMessageSnapshot> HistoryNewestFirst(int maxCount)
    {
        return _history.NewestFirst(maxCount);
    }

    private void RestoreSearchState()
    {
        IsSearchVisible = true;
        if (_searchStateCaptured)
        {
            IsExpanded = _expandedBeforeSearch;
            _searchStateCaptured = false;
        }

        foreach (var child in Children)
        {
            child.RestoreSearchState();
        }
    }

    private static string Pluralize(string word, int count) => count == 1 ? word : word + "s";
}
