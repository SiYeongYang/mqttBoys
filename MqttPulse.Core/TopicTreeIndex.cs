namespace MqttPulse.Core;

public sealed class TopicTreeIndex
{
    private readonly Dictionary<string, TopicNode> _topicsByFullTopic = new(StringComparer.Ordinal);
    private readonly int _maxHistoryPerTopic;

    public TopicTreeIndex(int maxHistoryPerTopic)
    {
        if (maxHistoryPerTopic <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHistoryPerTopic), "History capacity must be greater than zero.");
        }

        _maxHistoryPerTopic = maxHistoryPerTopic;
        Root = new TopicNode(string.Empty, string.Empty, maxHistoryPerTopic);
    }

    public TopicNode Root { get; }

    public int TopicCount { get; private set; }

    public long MessageCount { get; private set; }

    public TopicNode Ingest(MqttMessageSnapshot message)
    {
        if (string.IsNullOrWhiteSpace(message.Topic))
        {
            throw new ArgumentException("MQTT topic must not be empty.", nameof(message));
        }

        MessageCount++;

        var segments = message.Topic.Split('/', StringSplitOptions.None);
        var current = Root;
        var path = string.Empty;

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            path = path.Length == 0 ? segment : $"{path}/{segment}";
            current = current.GetOrCreateChild(segment, path, _maxHistoryPerTopic);
            if (index < segments.Length - 1)
            {
                current.RecordAggregate(message);
            }
        }

        // Leaf history is separated from parent aggregates so expanding a branch does not duplicate payloads.
        if (current.RecordLeaf(message))
        {
            TopicCount++;
            _topicsByFullTopic[message.Topic] = current;
        }

        return current;
    }

    public TopicNode? GetTopic(string fullTopic)
    {
        _topicsByFullTopic.TryGetValue(fullTopic, out var topic);
        return topic;
    }

    public IReadOnlyList<TopicNode> SearchTopics(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _topicsByFullTopic.Values
                .OrderBy(x => x.FullTopic, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return _topicsByFullTopic.Values
            .Where(x => x.FullTopic.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.FullTopic, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
