using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MqttPulse.Core;

namespace MqttPulse.App.Controls;

public sealed class JsonPayloadViewer : RichTextBox
{
    private const int HighlightLimit = 64_000;
    private const int InteractiveLimit = 256_000;
    private const int SearchMatchLimit = 10_000;
    private static readonly Brush TextBrush = Frozen("#17211F");
    private static readonly Brush KeyBrush = Frozen("#0A5C9C");
    private static readonly Brush StringBrush = Frozen("#167245");
    private static readonly Brush NumberBrush = Frozen("#8A4A00");
    private static readonly Brush LiteralBrush = Frozen("#8B2F75");
    private static readonly Brush PunctuationBrush = Frozen("#65716E");
    private static readonly Brush ChartActionBrush = Frozen("#176E63");
    private static readonly Brush SearchMatchBrush = Frozen("#FFE79A");
    private static readonly Brush ActiveSearchMatchBrush = Frozen("#F2B84B");
    private readonly List<SearchMatch> _searchMatches = new();
    private readonly Dictionary<int, List<Run>> _searchMatchRuns = new();
    private string _searchQuery = string.Empty;
    private int _activeSearchMatchIndex = -1;
    private bool _searchResultsTruncated;
    private bool _bringActiveSearchMatchIntoView;
    private bool _rendering;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(JsonPayloadViewer),
        new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty EnableChartActionsProperty = DependencyProperty.Register(
        nameof(EnableChartActions),
        typeof(bool),
        typeof(JsonPayloadViewer),
        new FrameworkPropertyMetadata(false, OnEnableChartActionsChanged));

    public JsonPayloadViewer()
    {
        IsReadOnly = true;
        IsDocumentEnabled = false;
        IsUndoEnabled = false;
        UndoLimit = 0;
        BorderThickness = new Thickness(1);
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Document = CreateDocument(string.Empty);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool EnableChartActions
    {
        get => (bool)GetValue(EnableChartActionsProperty);
        set => SetValue(EnableChartActionsProperty, value);
    }

    public string SearchQuery => _searchQuery;

    public int SearchMatchCount => _searchMatches.Count;

    public int ActiveSearchMatchNumber => _activeSearchMatchIndex < 0
        ? 0
        : _activeSearchMatchIndex + 1;

    public bool SearchResultsTruncated => _searchResultsTruncated;

    public string SearchResultText => SearchMatchCount == 0
        ? "0 / 0"
        : $"{ActiveSearchMatchNumber} / {SearchMatchCount}{(SearchResultsTruncated ? "+" : string.Empty)}";

    public event EventHandler<JsonChartRequestedEventArgs>? ChartRequested;

    public event EventHandler? SearchStateChanged;

    public void SetSearchQuery(string? query)
    {
        var normalized = query ?? string.Empty;
        if (_searchQuery.Equals(normalized, StringComparison.Ordinal))
        {
            return;
        }

        _searchQuery = normalized;
        RefreshSearchMatches(resetActiveMatch: true);
        _bringActiveSearchMatchIntoView = SearchMatchCount > 0;
        Render(Text);
        RaiseSearchStateChanged();
    }

    public void MoveSearchMatch(int offset)
    {
        if (SearchMatchCount == 0 || offset == 0)
        {
            return;
        }

        var previous = _activeSearchMatchIndex;
        _activeSearchMatchIndex = (_activeSearchMatchIndex + offset) % SearchMatchCount;
        if (_activeSearchMatchIndex < 0)
        {
            _activeSearchMatchIndex += SearchMatchCount;
        }

        ApplySearchMatchBrush(previous, SearchMatchBrush);
        ApplySearchMatchBrush(_activeSearchMatchIndex, ActiveSearchMatchBrush);
        BringSearchMatchIntoView(_activeSearchMatchIndex);
        RaiseSearchStateChanged();
    }

    public void ClearSearch()
    {
        if (_searchQuery.Length == 0 && SearchMatchCount == 0)
        {
            return;
        }

        _searchQuery = string.Empty;
        _searchMatches.Clear();
        _activeSearchMatchIndex = -1;
        _searchResultsTruncated = false;
        _bringActiveSearchMatchIntoView = false;
        Render(Text);
        RaiseSearchStateChanged();
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;

        if (e.Delta == 0)
        {
            return;
        }

        var direction = e.Delta > 0 ? -1 : 1;
        var wheelNotches = Math.Max(1.0, Math.Abs(e.Delta) / 120.0);
        var step = Math.Max(FontSize * 3.0, 36.0) * wheelNotches;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            if (ExtentWidth <= ViewportWidth)
            {
                return;
            }

            var maxHorizontalOffset = Math.Max(0, ExtentWidth - ViewportWidth);
            var horizontalTarget = Math.Clamp(HorizontalOffset + (direction * step), 0, maxHorizontalOffset);
            ScrollToHorizontalOffset(horizontalTarget);
            return;
        }

        if (ExtentHeight <= ViewportHeight)
        {
            return;
        }

        var maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        var targetOffset = Math.Clamp(VerticalOffset + (direction * step), 0, maxOffset);
        ScrollToVerticalOffset(targetOffset);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonPayloadViewer viewer)
        {
            viewer.RefreshSearchMatches(resetActiveMatch: true);
            viewer._bringActiveSearchMatchIntoView = viewer.SearchMatchCount > 0;
            viewer.Render((string?)e.NewValue ?? string.Empty);
            viewer.RaiseSearchStateChanged();
        }
    }

    private static void OnEnableChartActionsChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not JsonPayloadViewer viewer)
        {
            return;
        }

        viewer.IsDocumentEnabled = (bool)e.NewValue;
        viewer.RefreshSearchMatches(resetActiveMatch: true);
        viewer.Render(viewer.Text);
        viewer.RaiseSearchStateChanged();
    }

    private void Render(string text)
    {
        if (_rendering)
        {
            return;
        }

        var verticalOffset = VerticalOffset;
        var horizontalOffset = HorizontalOffset;
        var wasAtBottom = IsNearBottom();

        try
        {
            _rendering = true;
            _searchMatchRuns.Clear();
            var paragraph = CreateParagraph(text);
            BeginChange();
            try
            {
                Document.Blocks.Clear();
                Document.Blocks.Add(paragraph);
            }
            finally
            {
                EndChange();
            }
        }
        finally
        {
            _rendering = false;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_bringActiveSearchMatchIntoView && SearchMatchCount > 0)
            {
                _bringActiveSearchMatchIntoView = false;
                BringSearchMatchIntoView(_activeSearchMatchIndex);
                return;
            }

            if (wasAtBottom)
            {
                ScrollToEnd();
                return;
            }

            ScrollToVerticalOffset(Math.Max(0, verticalOffset));
            ScrollToHorizontalOffset(Math.Max(0, horizontalOffset));
        });
    }

    private bool IsNearBottom()
    {
        return ExtentHeight <= 0 || VerticalOffset + ViewportHeight >= ExtentHeight - 2;
    }

    private FlowDocument CreateDocument(string text)
    {
        return new FlowDocument(CreateParagraph(text))
        {
            PagePadding = new Thickness(8, 4, 8, 4),
            FontFamily = FontFamily,
            FontSize = FontSize,
            PageWidth = 4096
        };
    }

    private Paragraph CreateParagraph(string text)
    {
        if (EnableChartActions
            && text.Length <= InteractiveLimit
            && JsonDisplayFormatter.TryBuild(text, out var lines))
        {
            return CreateInteractiveParagraph(lines);
        }

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            FontFamily = FontFamily,
            FontSize = FontSize
        };

        if (LooksLikeJson(text) && text.Length <= HighlightLimit)
        {
            AddJsonRuns(paragraph, text, baseOffset: 0);
        }
        else
        {
            AddRun(paragraph, text, 0, text.Length, TextBrush, baseOffset: 0);
        }

        return paragraph;
    }

    private Paragraph CreateInteractiveParagraph(IReadOnlyList<JsonDisplayLine> lines)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            FontFamily = FontFamily,
            FontSize = FontSize
        };

        var textOffset = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Metric is { } metric)
            {
                var action = new Hyperlink(new Run("↗"))
                {
                    Tag = metric,
                    ToolTip = $"Add {metric.DisplayPath} to Charts",
                    Cursor = Cursors.Hand,
                    Foreground = ChartActionBrush,
                    FontWeight = FontWeights.SemiBold,
                    TextDecorations = null
                };
                action.Click += ChartAction_Click;
                paragraph.Inlines.Add(action);
                paragraph.Inlines.Add(new Run(" "));
            }
            else
            {
                paragraph.Inlines.Add(new Run("  "));
            }

            AddJsonRuns(paragraph, line.Text, textOffset);
            if (index < lines.Count - 1)
            {
                paragraph.Inlines.Add(new LineBreak());
                textOffset += line.Text.Length + Environment.NewLine.Length;
            }
            else
            {
                textOffset += line.Text.Length;
            }
        }

        return paragraph;
    }

    private void ChartAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { Tag: JsonScalarMetric metric })
        {
            ChartRequested?.Invoke(this, new JsonChartRequestedEventArgs(metric));
            e.Handled = true;
        }
    }

    private void AddJsonRuns(Paragraph paragraph, string text, int baseOffset)
    {
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                var start = index;
                index = ReadString(text, index);
                AddRun(
                    paragraph,
                    text,
                    start,
                    index,
                    IsKey(text, index) ? KeyBrush : StringBrush,
                    baseOffset);
                continue;
            }

            if (ch == '-' || char.IsDigit(ch))
            {
                var start = index;
                index = ReadNumber(text, index);
                AddRun(paragraph, text, start, index, NumberBrush, baseOffset);
                continue;
            }

            if (StartsWithLiteral(text, index, "true") || StartsWithLiteral(text, index, "false") || StartsWithLiteral(text, index, "null"))
            {
                var start = index;
                index += text[index] == 't' || text[index] == 'n' ? 4 : 5;
                AddRun(paragraph, text, start, index, LiteralBrush, baseOffset);
                continue;
            }

            if (IsPunctuation(ch))
            {
                AddRun(paragraph, text, index, index + 1, PunctuationBrush, baseOffset);
                index++;
                continue;
            }

            var plainStart = index;
            while (index < text.Length
                   && text[index] != '"'
                   && text[index] != '-'
                   && !char.IsDigit(text[index])
                   && !IsPunctuation(text[index])
                   && !StartsWithLiteral(text, index, "true")
                   && !StartsWithLiteral(text, index, "false")
                   && !StartsWithLiteral(text, index, "null"))
            {
                index++;
            }

            AddRun(paragraph, text, plainStart, index, TextBrush, baseOffset);
        }
    }

    private static int ReadString(string text, int start)
    {
        var escaped = false;
        for (var i = start + 1; i < text.Length; i++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[i] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[i] == '"')
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    private static int ReadNumber(string text, int start)
    {
        var i = start;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] is '-' or '+' or '.' or 'e' or 'E'))
        {
            i++;
        }

        return i;
    }

    private static bool IsKey(string text, int afterString)
    {
        for (var i = afterString; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                continue;
            }

            return text[i] == ':';
        }

        return false;
    }

    private static bool StartsWithLiteral(string text, int index, string literal)
    {
        return index + literal.Length <= text.Length
               && string.CompareOrdinal(text, index, literal, 0, literal.Length) == 0;
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.AsSpan().Trim();
        return trimmed.Length >= 2
               && ((trimmed[0] == '{' && trimmed[^1] == '}') || (trimmed[0] == '[' && trimmed[^1] == ']'));
    }

    private static bool IsPunctuation(char value) => value is '{' or '}' or '[' or ']' or ':' or ',';

    private void AddRun(
        Paragraph paragraph,
        string text,
        int start,
        int end,
        Brush brush,
        int baseOffset)
    {
        if (end <= start)
        {
            return;
        }

        var globalStart = baseOffset + start;
        var globalEnd = baseOffset + end;
        var position = globalStart;
        var matchIndex = FindFirstOverlappingSearchMatch(globalStart);

        while (matchIndex < _searchMatches.Count)
        {
            var match = _searchMatches[matchIndex];
            if (match.Start >= globalEnd)
            {
                break;
            }

            var highlightStart = Math.Max(position, match.Start);
            var highlightEnd = Math.Min(globalEnd, match.End);
            if (highlightStart > position)
            {
                AddTextRun(
                    paragraph,
                    text,
                    position - baseOffset,
                    highlightStart - baseOffset,
                    brush,
                    background: null,
                    matchIndex: null);
            }

            if (highlightEnd > highlightStart)
            {
                AddTextRun(
                    paragraph,
                    text,
                    highlightStart - baseOffset,
                    highlightEnd - baseOffset,
                    brush,
                    matchIndex == _activeSearchMatchIndex
                        ? ActiveSearchMatchBrush
                        : SearchMatchBrush,
                    matchIndex);
                position = highlightEnd;
            }

            if (match.End <= globalEnd)
            {
                matchIndex++;
            }
            else
            {
                break;
            }
        }

        if (position < globalEnd)
        {
            AddTextRun(
                paragraph,
                text,
                position - baseOffset,
                globalEnd - baseOffset,
                brush,
                background: null,
                matchIndex: null);
        }
    }

    private void AddTextRun(
        Paragraph paragraph,
        string text,
        int start,
        int end,
        Brush foreground,
        Brush? background,
        int? matchIndex)
    {
        if (end <= start)
        {
            return;
        }

        var run = new Run(text[start..end])
        {
            Foreground = foreground,
            Background = background
        };
        paragraph.Inlines.Add(run);

        if (matchIndex is not { } index)
        {
            return;
        }

        if (!_searchMatchRuns.TryGetValue(index, out var runs))
        {
            runs = new List<Run>();
            _searchMatchRuns.Add(index, runs);
        }

        runs.Add(run);
    }

    private int FindFirstOverlappingSearchMatch(int textOffset)
    {
        var low = 0;
        var high = _searchMatches.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_searchMatches[middle].End <= textOffset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void RefreshSearchMatches(bool resetActiveMatch)
    {
        _searchMatches.Clear();
        _searchResultsTruncated = false;

        if (_searchQuery.Length == 0)
        {
            _activeSearchMatchIndex = -1;
            return;
        }

        var searchableText = BuildSearchableText(Text);
        var offset = 0;
        while (offset <= searchableText.Length - _searchQuery.Length)
        {
            var match = searchableText.IndexOf(
                _searchQuery,
                offset,
                StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                break;
            }

            if (_searchMatches.Count >= SearchMatchLimit)
            {
                _searchResultsTruncated = true;
                break;
            }

            _searchMatches.Add(new SearchMatch(match, _searchQuery.Length));
            offset = match + Math.Max(_searchQuery.Length, 1);
        }

        if (_searchMatches.Count == 0)
        {
            _activeSearchMatchIndex = -1;
            return;
        }

        _activeSearchMatchIndex = resetActiveMatch
            ? 0
            : Math.Clamp(_activeSearchMatchIndex, 0, _searchMatches.Count - 1);
    }

    private string BuildSearchableText(string text)
    {
        if (EnableChartActions
            && text.Length <= InteractiveLimit
            && JsonDisplayFormatter.TryBuild(text, out var lines))
        {
            return string.Join(Environment.NewLine, lines.Select(line => line.Text));
        }

        return text;
    }

    private void ApplySearchMatchBrush(int matchIndex, Brush brush)
    {
        if (matchIndex < 0 || !_searchMatchRuns.TryGetValue(matchIndex, out var runs))
        {
            return;
        }

        foreach (var run in runs)
        {
            run.Background = brush;
        }
    }

    private void BringSearchMatchIntoView(int matchIndex)
    {
        if (matchIndex < 0
            || !_searchMatchRuns.TryGetValue(matchIndex, out var runs)
            || runs.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(runs[0].BringIntoView));
    }

    private void RaiseSearchStateChanged()
    {
        SearchStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private readonly record struct SearchMatch(int Start, int Length)
    {
        public int End => Start + Length;
    }
}

public sealed class JsonChartRequestedEventArgs(JsonScalarMetric metric) : EventArgs
{
    public JsonScalarMetric Metric { get; } = metric;
}
