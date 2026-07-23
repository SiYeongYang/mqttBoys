namespace MqttPulse.Core;

public sealed class BoundedMessageHistory
{
    private const int DefaultMaxPayloadCharacters = 1_000_000;
    private const int DefaultMinimumRetainedCount = 5;
    private readonly MqttMessageSnapshot?[] _items;
    private readonly int _maxPayloadCharacters;
    private readonly int _minimumRetainedCount;
    private int _nextIndex;
    private long _payloadCharacters;

    public BoundedMessageHistory(
        int capacity,
        int maxPayloadCharacters = DefaultMaxPayloadCharacters,
        int minimumRetainedCount = DefaultMinimumRetainedCount)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "History capacity must be greater than zero.");
        }

        if (maxPayloadCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadCharacters), "Payload budget must be greater than zero.");
        }

        if (minimumRetainedCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRetainedCount), "Minimum retained count must be greater than zero.");
        }

        _items = new MqttMessageSnapshot?[capacity];
        _maxPayloadCharacters = maxPayloadCharacters;
        _minimumRetainedCount = Math.Min(capacity, minimumRetainedCount);
    }

    public int Capacity => _items.Length;

    public int Count { get; private set; }

    public long PayloadCharacters => _payloadCharacters;

    public void Add(MqttMessageSnapshot message)
    {
        if (Count == _items.Length && _items[_nextIndex] is { } overwritten)
        {
            _payloadCharacters -= overwritten.PayloadLength;
        }

        _items[_nextIndex] = message;
        _payloadCharacters += message.PayloadLength;
        _nextIndex = (_nextIndex + 1) % _items.Length;
        if (Count < _items.Length)
        {
            Count++;
        }

        while (Count > _minimumRetainedCount && _payloadCharacters > _maxPayloadCharacters)
        {
            var oldestIndex = (_nextIndex - Count + _items.Length) % _items.Length;
            if (_items[oldestIndex] is { } oldest)
            {
                _payloadCharacters -= oldest.PayloadLength;
                _items[oldestIndex] = null;
            }

            Count--;
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
            result.Add(_items[index]!);
        }

        return result;
    }

    public IReadOnlyList<MqttMessageSnapshot> OldestFirst()
    {
        var result = new List<MqttMessageSnapshot>(Count);

        for (var i = Count - 1; i >= 0; i--)
        {
            var index = (_nextIndex - 1 - i + _items.Length) % _items.Length;
            result.Add(_items[index]!);
        }

        return result;
    }
}
