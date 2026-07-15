namespace MqttPulse.Core;

public sealed class BoundedMessageHistory
{
    private readonly MqttMessageSnapshot[] _items;
    private int _nextIndex;

    public BoundedMessageHistory(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "History capacity must be greater than zero.");
        }

        _items = new MqttMessageSnapshot[capacity];
    }

    public int Capacity => _items.Length;

    public int Count { get; private set; }

    public void Add(MqttMessageSnapshot message)
    {
        // Keep history bounded so rapid, large payloads cannot grow memory or resize the UI forever.
        _items[_nextIndex] = message;
        _nextIndex = (_nextIndex + 1) % _items.Length;
        if (Count < _items.Length)
        {
            Count++;
        }
    }

    public IReadOnlyList<MqttMessageSnapshot> NewestFirst()
    {
        return NewestFirst(Count);
    }

    public IReadOnlyList<MqttMessageSnapshot> NewestFirst(int maxCount)
    {
        var count = Math.Min(Count, Math.Max(0, maxCount));
        var result = new List<MqttMessageSnapshot>(count);

        for (var i = 0; i < count; i++)
        {
            var index = (_nextIndex - 1 - i + _items.Length) % _items.Length;
            result.Add(_items[index]);
        }

        return result;
    }

    public IReadOnlyList<MqttMessageSnapshot> OldestFirst()
    {
        var result = new List<MqttMessageSnapshot>(Count);

        for (var i = Count - 1; i >= 0; i--)
        {
            var index = (_nextIndex - 1 - i + _items.Length) % _items.Length;
            result.Add(_items[index]);
        }

        return result;
    }
}