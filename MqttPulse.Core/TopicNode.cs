namespace MqttPulse.Core;

public sealed class TopicNode
{
    private readonly Dictionary<string, TopicNode> _children = new(StringComparer.Ordinal);

    internal TopicNode(string name, string fullTopic, int historyCapacity)
    {
        Name = name;
        FullTopic = fullTopic;
        History = new BoundedMessageHistory(historyCapacity);
    }

    public string Name { get; }

    public string FullTopic { get; }

    public IReadOnlyCollection<TopicNode> Children => _children.Values;

    public int MessageCount { get; private set; }

    public MqttMessageSnapshot? LastMessage { get; private set; }

    public BoundedMessageHistory History { get; }

    public bool IsLeafTopic { get; private set; }

    internal TopicNode GetOrCreateChild(string name, string fullTopic, int historyCapacity)
    {
        if (_children.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var created = new TopicNode(name, fullTopic, historyCapacity);
        _children.Add(name, created);
        return created;
    }

    internal void RecordAggregate(MqttMessageSnapshot message)
    {
        MessageCount++;
        LastMessage = message;
    }

    internal bool RecordLeaf(MqttMessageSnapshot message)
    {
        RecordAggregate(message);
        History.Add(message);

        if (IsLeafTopic)
        {
            return false;
        }

        IsLeafTopic = true;
        return true;
    }
}
