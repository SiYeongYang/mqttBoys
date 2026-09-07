using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MqttPulse.Core;

public enum AsciiByteOrder
{
    BigEndian,
    LittleEndian
}

public static class PackedAsciiDecoder
{
    public const int MaxWords = 2_048;

    public static bool CanDecode(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return TryReadWord(value, out _);
        }

        return value.GetArrayLength() is > 0 and <= MaxWords
            && value.EnumerateArray().All(item => TryReadWord(item, out _));
    }

    public static bool TryDecode(JsonElement value, AsciiByteOrder order, out string text)
    {
        text = string.Empty;
        if (!CanDecode(value)) return false;

        var output = new StringBuilder();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) AppendWord(item);
        }
        else
        {
            AppendWord(value);
        }

        text = output.ToString();
        return true;

        void AppendWord(JsonElement element)
        {
            TryReadWord(element, out var word);
            var high = (byte)(word >> 8);
            var low = (byte)(word & 255);
            AppendByte(output, order == AsciiByteOrder.LittleEndian ? low : high);
            AppendByte(output, order == AsciiByteOrder.LittleEndian ? high : low);
        }
    }

    private static bool TryReadWord(JsonElement value, out ushort word)
    {
        word = 0;
        decimal number;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDecimal(out number)) return false;
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            if (!decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;
        }
        else
        {
            return false;
        }

        if (number < short.MinValue || number > ushort.MaxValue || decimal.Truncate(number) != number) return false;
        word = unchecked((ushort)(int)number);
        return true;
    }

    private static void AppendByte(StringBuilder output, byte value)
    {
        if (value <= 127)
        {
            // The JSON serializer escapes actual ASCII control characters.
            output.Append((char)value);
        }
        else
        {
            output.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
    }
}
