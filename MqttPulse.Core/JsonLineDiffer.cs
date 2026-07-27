namespace MqttPulse.Core;

public enum JsonDiffKind
{
    Unchanged,
    Removed,
    Added
}

public sealed record JsonDiffLine(string Text, JsonDiffKind Kind);

public sealed record JsonLineDiffResult(
    IReadOnlyList<JsonDiffLine> Lines,
    int AddedLineCount,
    int RemovedLineCount,
    bool IsSimplified);

public static class JsonLineDiffer
{
    private const long MaxMatrixCells = 1_000_000;

    public static JsonLineDiffResult Compare(string baseline, string current)
    {
        var oldLines = SplitLines(baseline);
        var newLines = SplitLines(current);
        if ((long)(oldLines.Length + 1) * (newLines.Length + 1) > MaxMatrixCells)
        {
            return CompareLarge(oldLines, newLines);
        }

        var lengths = new ushort[oldLines.Length + 1, newLines.Length + 1];
        for (var oldIndex = oldLines.Length - 1; oldIndex >= 0; oldIndex--)
        {
            for (var newIndex = newLines.Length - 1; newIndex >= 0; newIndex--)
            {
                lengths[oldIndex, newIndex] = oldLines[oldIndex].Equals(
                    newLines[newIndex],
                    StringComparison.Ordinal)
                    ? (ushort)(lengths[oldIndex + 1, newIndex + 1] + 1)
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
            }
        }

        var lines = new List<JsonDiffLine>(oldLines.Length + newLines.Length);
        var added = 0;
        var removed = 0;
        var i = 0;
        var j = 0;
        while (i < oldLines.Length && j < newLines.Length)
        {
            if (oldLines[i].Equals(newLines[j], StringComparison.Ordinal))
            {
                lines.Add(new JsonDiffLine(oldLines[i], JsonDiffKind.Unchanged));
                i++;
                j++;
                continue;
            }

            if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                lines.Add(new JsonDiffLine(oldLines[i++], JsonDiffKind.Removed));
                removed++;
            }
            else
            {
                lines.Add(new JsonDiffLine(newLines[j++], JsonDiffKind.Added));
                added++;
            }
        }

        while (i < oldLines.Length)
        {
            lines.Add(new JsonDiffLine(oldLines[i++], JsonDiffKind.Removed));
            removed++;
        }

        while (j < newLines.Length)
        {
            lines.Add(new JsonDiffLine(newLines[j++], JsonDiffKind.Added));
            added++;
        }

        return new JsonLineDiffResult(lines, added, removed, IsSimplified: false);
    }

    private static JsonLineDiffResult CompareLarge(string[] oldLines, string[] newLines)
    {
        var prefixLength = 0;
        while (prefixLength < oldLines.Length
               && prefixLength < newLines.Length
               && oldLines[prefixLength].Equals(newLines[prefixLength], StringComparison.Ordinal))
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < oldLines.Length - prefixLength
               && suffixLength < newLines.Length - prefixLength
               && oldLines[^(suffixLength + 1)].Equals(newLines[^(suffixLength + 1)], StringComparison.Ordinal))
        {
            suffixLength++;
        }

        var lines = new List<JsonDiffLine>(oldLines.Length + newLines.Length);
        for (var index = 0; index < prefixLength; index++)
        {
            lines.Add(new JsonDiffLine(oldLines[index], JsonDiffKind.Unchanged));
        }

        var oldMiddleEnd = oldLines.Length - suffixLength;
        for (var index = prefixLength; index < oldMiddleEnd; index++)
        {
            lines.Add(new JsonDiffLine(oldLines[index], JsonDiffKind.Removed));
        }

        var newMiddleEnd = newLines.Length - suffixLength;
        for (var index = prefixLength; index < newMiddleEnd; index++)
        {
            lines.Add(new JsonDiffLine(newLines[index], JsonDiffKind.Added));
        }

        for (var index = oldLines.Length - suffixLength; index < oldLines.Length; index++)
        {
            lines.Add(new JsonDiffLine(oldLines[index], JsonDiffKind.Unchanged));
        }

        return new JsonLineDiffResult(
            lines,
            newMiddleEnd - prefixLength,
            oldMiddleEnd - prefixLength,
            IsSimplified: true);
    }

    private static string[] SplitLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }
}
