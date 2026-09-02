using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MqttPulse.Core;

public static class PackedAsciiDecoder
{
    private const int DefaultMaxWords = 2_048;

    public static PackedAsciiDecodeResult Decode(
        string payload,
        int maxWords = DefaultMaxWords)
    {
        if (maxWords <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxWords),
                "Maximum word count must be greater than zero.");
        }

        var words = new List<PackedAsciiWord>(Math.Min(maxWords, 64));
        var truncated = TryCollectJsonWords(payload, words, maxWords);
        if (words.Count == 0 && !truncated)
        {
            TryCollectPlainNumber(payload, words);
        }

        if (words.Count == 0)
        {
            return new PackedAsciiDecodeResult(
                0,
                string.Empty,
                string.Empty,
                false,
                "ASCII decode (16-bit words)"
                + Environment.NewLine
                + Environment.NewLine
                + "No 16-bit integer values found."
                + Environment.NewLine
                + "Supported range: -32768 to 65535.");
        }

        var highByteFirst = new StringBuilder(words.Count * 2);
        var byteSwapped = new StringBuilder(words.Count * 2);
        foreach (var word in words)
        {
            AppendAscii(highByteFirst, word.HighByte);
            AppendAscii(highByteFirst, word.LowByte);
            AppendAscii(byteSwapped, word.LowByte);
            AppendAscii(byteSwapped, word.HighByte);
        }

        var highByteFirstText = highByteFirst.ToString();
        var byteSwappedText = byteSwapped.ToString();
        var display = BuildDisplayText(
            words,
            highByteFirstText,
            byteSwappedText,
            truncated);

        return new PackedAsciiDecodeResult(
            words.Count,
            highByteFirstText,
            byteSwappedText,
            truncated,
            display);
    }

    private static bool TryCollectJsonWords(
        string payload,
        ICollection<PackedAsciiWord> words,
        int maxWords)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return CollectJsonWords(document.RootElement, "$", words, maxWords);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool CollectJsonWords(
        JsonElement element,
        string path,
        ICollection<PackedAsciiWord> words,
        int maxWords)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (CollectJsonWords(
                            property.Value,
                            AppendPropertyPath(path, property.Name),
                            words,
                            maxWords))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (CollectJsonWords(item, $"{path}[{index}]", words, maxWords))
                    {
                        return true;
                    }

                    index++;
                }

                return false;

            case JsonValueKind.Number:
                if (TryGetInteger(element, out var number))
                {
                    return AddWord(path, number, words, maxWords);
                }

                return false;

            case JsonValueKind.String:
                if (long.TryParse(
                        element.GetString(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var numericString))
                {
                    return AddWord(path, numericString, words, maxWords);
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryGetInteger(JsonElement element, out long value)
    {
        if (element.TryGetInt64(out value))
        {
            return true;
        }

        if (element.TryGetDecimal(out var decimalValue)
            && decimal.Truncate(decimalValue) == decimalValue
            && decimalValue >= long.MinValue
            && decimalValue <= long.MaxValue)
        {
            value = decimal.ToInt64(decimalValue);
            return true;
        }

        value = 0;
        return false;
    }

    private static void TryCollectPlainNumber(
        string payload,
        ICollection<PackedAsciiWord> words)
    {
        if (long.TryParse(
                payload.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
            && TryToWord(value, out var word))
        {
            words.Add(new PackedAsciiWord("$", value, word));
        }
    }

    private static bool AddWord(
        string path,
        long value,
        ICollection<PackedAsciiWord> words,
        int maxWords)
    {
        if (!TryToWord(value, out var word))
        {
            return false;
        }

        if (words.Count >= maxWords)
        {
            return true;
        }

        words.Add(new PackedAsciiWord(path, value, word));
        return false;
    }

    private static bool TryToWord(long value, out ushort word)
    {
        if (value is < short.MinValue or > ushort.MaxValue)
        {
            word = 0;
            return false;
        }

        word = value < 0
            ? unchecked((ushort)(short)value)
            : (ushort)value;
        return true;
    }

    private static string AppendPropertyPath(string path, string propertyName)
    {
        if (propertyName.All(value => char.IsLetterOrDigit(value) || value == '_'))
        {
            return $"{path}.{propertyName}";
        }

        return $"{path}[{JsonSerializer.Serialize(propertyName)}]";
    }

    private static string BuildDisplayText(
        IReadOnlyCollection<PackedAsciiWord> words,
        string highByteFirst,
        string byteSwapped,
        bool truncated)
    {
        var display = new StringBuilder(Math.Min(256_000, words.Count * 56 + 256));
        display.AppendLine("ASCII decode (16-bit words)");
        display.Append("Words: ").Append(words.Count);
        if (truncated)
        {
            display.Append('+');
        }

        display.AppendLine();
        display.AppendLine();
        display.Append("High byte first (BE): ").AppendLine(highByteFirst);
        display.Append("Byte-swapped (LE): ").AppendLine(byteSwapped);
        display.AppendLine();
        var pathWidth = Math.Min(
            48,
            Math.Max("Path".Length, words.Max(word => word.Path.Length)));
        display
            .Append("Path".PadRight(pathWidth)).Append(' ')
            .Append("Decimal".PadLeft(7)).Append(' ')
            .Append("Hex".PadRight(6)).Append(' ')
            .Append("BE".PadRight(8)).Append(' ')
            .AppendLine("Swap");

        foreach (var word in words)
        {
            var path = ShortenPath(word.Path, pathWidth);
            var highFirst = FormatAsciiPair(word.HighByte, word.LowByte);
            var swapped = FormatAsciiPair(word.LowByte, word.HighByte);
            display
                .Append(path.PadRight(pathWidth)).Append(' ')
                .Append(word.SourceValue.ToString(CultureInfo.InvariantCulture).PadLeft(7)).Append(' ')
                .Append("0x").Append(word.Word.ToString("X4", CultureInfo.InvariantCulture)).Append(' ')
                .Append(highFirst.PadRight(8)).Append(' ')
                .AppendLine(swapped);
        }

        if (truncated)
        {
            display.AppendLine();
            display.Append("... additional values omitted");
        }

        return display.ToString().TrimEnd();
    }

    private static string ShortenPath(string path, int width)
    {
        if (path.Length <= width)
        {
            return path;
        }

        var suffixLength = width - 3;
        return "..." + path[(path.Length - suffixLength)..];
    }

    private static string FormatAsciiPair(byte first, byte second)
    {
        var text = new StringBuilder(8);
        AppendAscii(text, first);
        AppendAscii(text, second);
        return text.ToString();
    }

    private static void AppendAscii(StringBuilder target, byte value)
    {
        switch (value)
        {
            case 0:
                target.Append("\\0");
                break;
            case (byte)'\t':
                target.Append("\\t");
                break;
            case (byte)'\r':
                target.Append("\\r");
                break;
            case (byte)'\n':
                target.Append("\\n");
                break;
            case >= 0x20 and <= 0x7E:
                target.Append((char)value);
                break;
            default:
                target.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
                break;
        }
    }

    private readonly record struct PackedAsciiWord(
        string Path,
        long SourceValue,
        ushort Word)
    {
        public byte HighByte => (byte)(Word >> 8);

        public byte LowByte => (byte)(Word & 0xFF);
    }
}

public sealed record PackedAsciiDecodeResult(
    int WordCount,
    string HighByteFirstText,
    string ByteSwappedText,
    bool Truncated,
    string DisplayText);
