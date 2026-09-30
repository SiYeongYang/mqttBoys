using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using MqttPulse.Core;

namespace MqttPulse.App.Controls;

public sealed class VirtualPayloadViewer : TextEditor
{
    private PreparationRequest? _pending;
    private PreparedPayload? _prepared;
    private int _workerRunning;
    private int _generation;
    private long _version;
    private long _appliedVersion;
    private string _query = string.Empty;
    private SearchMatch[] _matches = Array.Empty<SearchMatch>();
    private int _activeMatch;
    private bool _searchTruncated;
    private bool _searching;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource _documentCancellation = new();

    public static readonly DependencyProperty PayloadTextProperty = DependencyProperty.Register(
        nameof(PayloadText), typeof(string), typeof(VirtualPayloadViewer),
        new PropertyMetadata(string.Empty, (d, _) => ((VirtualPayloadViewer)d).QueueDocument()));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(VirtualPayloadViewer),
        new PropertyMetadata(false, (d, _) =>
        {
            var viewer = (VirtualPayloadViewer)d;
            viewer._generation++;
            viewer._pending = null;
            if (viewer.IsActive) viewer.QueueDocument();
        }));
    public static readonly DependencyProperty InspectionScopeProperty = DependencyProperty.Register(
        nameof(InspectionScope), typeof(object), typeof(VirtualPayloadViewer),
        new PropertyMetadata(null, (d, _) =>
        {
            var viewer = (VirtualPayloadViewer)d;
            viewer._generation++;
            viewer._prepared = null;
            viewer.Document = new TextDocument();
            viewer.SetValue(DisplayTextPropertyKey, string.Empty);
            viewer.ScrollToVerticalOffset(0);
            viewer.ScrollToHorizontalOffset(0);
            viewer.QueueDocument();
        }));
    public static readonly DependencyProperty BaselineTextProperty = DependencyProperty.Register(
        nameof(BaselineText), typeof(string), typeof(VirtualPayloadViewer),
        new PropertyMetadata(string.Empty, (d, _) => ((VirtualPayloadViewer)d).QueueDocument()));
    public static readonly DependencyProperty UseDiffProperty = DependencyProperty.Register(
        nameof(UseDiff), typeof(bool), typeof(VirtualPayloadViewer),
        new PropertyMetadata(false, (d, _) =>
        {
            var viewer = (VirtualPayloadViewer)d;
            viewer._generation++;
            viewer.QueueDocument();
        }));
    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(VirtualPayloadViewer), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;
    public string DisplayText => (string)GetValue(DisplayTextProperty);
    public string PayloadText { get => (string)GetValue(PayloadTextProperty); set => SetValue(PayloadTextProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public object? InspectionScope { get => GetValue(InspectionScopeProperty); set => SetValue(InspectionScopeProperty, value); }
    public string BaselineText { get => (string)GetValue(BaselineTextProperty); set => SetValue(BaselineTextProperty, value); }
    public bool UseDiff { get => (bool)GetValue(UseDiffProperty); set => SetValue(UseDiffProperty, value); }
    public int SearchMatchCount => _matches.Length;
    public string SelectedSourceText
    {
        get
        {
            if (_prepared is not { } prepared || SelectionLength == 0) return string.Empty;
            var start = prepared.ToSourceOffset(SelectionStart);
            var end = prepared.ToSourceOffset(SelectionStart + SelectionLength);
            return prepared.Source[start..end];
        }
    }
    public string SearchResultText => _searching ? "..."
        : _matches.Length == 0 ? "0 / 0" : $"{_activeMatch + 1} / {_matches.Length}{(_searchTruncated ? "+" : "")}";
    public event EventHandler? SearchStateChanged;

    public VirtualPayloadViewer()
    {
        IsReadOnly = true;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Document.UndoStack.SizeLimit = 0;
        TextArea.TextView.LineTransformers.Add(new PayloadColorizer(this));
        Loaded += (_, _) =>
        {
            if (_documentCancellation.IsCancellationRequested) _documentCancellation = new CancellationTokenSource();
            QueueDocument();
        };
        Unloaded += (_, _) =>
        {
            _generation++;
            _documentCancellation.Cancel();
            Interlocked.Exchange(ref _pending, null);
            _searchCancellation?.Cancel();
        };
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
        {
            if (_prepared is not null && SelectionLength > 0)
            {
                Clipboard.SetText(SelectedSourceText);
            }
            e.Handled = true;
        }, (_, e) => { e.CanExecute = SelectionLength > 0; e.Handled = true; }));
    }

    private void QueueDocument()
    {
        if (!IsActive || !IsLoaded) return;
        Interlocked.Exchange(ref _pending, new PreparationRequest(PayloadText, BaselineText, UseDiff, _generation, ++_version));
        StartWorker();
    }

    private void StartWorker()
    {
        if (Interlocked.CompareExchange(ref _workerRunning, 1, 0) != 0) return;
        var token = _documentCancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (Interlocked.Exchange(ref _pending, null) is { } request)
                {
                    var text = request.Text;
                    if (request.UseDiff)
                    {
                        var diff = JsonLineDiffer.Compare(request.Baseline, request.Text);
                        text = string.Join(Environment.NewLine, diff.Lines.Select(line =>
                            (line.Kind == JsonDiffKind.Added ? "+ " : line.Kind == JsonDiffKind.Removed ? "- " : "  ") + line.Text))
                            + $"{Environment.NewLine}Comparing with previous message: + {diff.AddedLineCount} lines, - {diff.RemovedLineCount} lines";
                    }
                    var prepared = PreparedPayload.Build(text);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!IsActive || !IsLoaded || request.Generation != _generation || request.Version <= _appliedVersion) return;
                        var vertical = VerticalOffset;
                        var horizontal = HorizontalOffset;
                        prepared.Document.SetOwnerThread(Thread.CurrentThread);
                        _prepared = prepared;
                        _appliedVersion = request.Version;
                        Document = prepared.Document;
                        SetValue(DisplayTextPropertyKey, prepared.Source);
                        ScrollToVerticalOffset(vertical);
                        ScrollToHorizontalOffset(horizontal);
                        StartSearch();
                    }, DispatcherPriority.Background, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
            finally
            {
                Interlocked.Exchange(ref _workerRunning, 0);
                if (Volatile.Read(ref _pending) is not null) StartWorker();
            }
        });
    }

    public void SetSearchQuery(string query)
    {
        if (_query == query) return;
        _query = query;
        StartSearch();
    }

    public void ClearSearch() => SetSearchQuery(string.Empty);

    private void StartSearch()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _matches = Array.Empty<SearchMatch>();
        _activeMatch = 0;
        _searchTruncated = false;
        if (_query.Length == 0 || _prepared is null || !IsActive)
        {
            _searching = false;
            TextArea.TextView.Redraw();
            SearchStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        _searching = true;
        SearchStateChanged?.Invoke(this, EventArgs.Empty);
        _ = SearchAsync(_prepared, _query, cancellation.Token);
    }

    private async Task SearchAsync(PreparedPayload prepared, string query, CancellationToken token)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var matches = new List<SearchMatch>();
                var offset = 0;
                while (offset <= prepared.Source.Length - query.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var found = prepared.Source.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    if (matches.Count == 10_000) return (Matches: matches.ToArray(), Truncated: true);
                    var start = prepared.ToDocumentOffset(found);
                    var end = prepared.ToDocumentOffset(found + query.Length);
                    matches.Add(new SearchMatch(start, end));
                    offset = found + query.Length;
                }
                return (Matches: matches.ToArray(), Truncated: false);
            }, token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(_prepared, prepared)) return;
                _matches = result.Matches;
                _searchTruncated = result.Truncated;
                _searching = false;
                TextArea.TextView.Redraw();
                BringMatchIntoView();
                SearchStateChanged?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Background, token);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }

    public void MoveSearchMatch(int offset)
    {
        if (_matches.Length == 0) return;
        _activeMatch = (_activeMatch + offset + _matches.Length) % _matches.Length;
        TextArea.TextView.Redraw();
        BringMatchIntoView();
        SearchStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BringMatchIntoView()
    {
        if (_matches.Length == 0) return;
        var match = _matches[_activeMatch];
        Select(match.Start, match.End - match.Start);
        var location = Document.GetLocation(match.Start);
        ScrollTo(location.Line, location.Column);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        var step = -e.Delta / 120.0 * Math.Max(36, FontSize * 3);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            ScrollToHorizontalOffset(Math.Max(0, HorizontalOffset + step));
        else
            ScrollToVerticalOffset(Math.Max(0, VerticalOffset + step));
    }

    private sealed record PreparationRequest(string Text, string Baseline, bool UseDiff, int Generation, long Version);
    private readonly record struct SearchMatch(int Start, int End);

    private sealed record PreparedPayload(string Source, TextDocument Document, int[] Breaks)
    {
        public static PreparedPayload Build(string source)
        {
            var output = new StringBuilder(source.Length);
            var breaks = new List<int>();
            var column = 0;
            for (var i = 0; i < source.Length; i++)
            {
                var ch = source[i];
                // Bound individual visual lines too: a single multi-megabyte JSON string must stay scrollable.
                if (column >= 2_048 && ch is not '\r' and not '\n' && !char.IsLowSurrogate(ch))
                {
                    breaks.Add(i);
                    output.Append('\n');
                    column = 0;
                }
                output.Append(ch);
                column = ch == '\n' ? 0 : column + 1;
            }
            var document = new TextDocument(output.ToString());
            document.UndoStack.SizeLimit = 0;
            document.SetOwnerThread(null);
            return new PreparedPayload(source, document, breaks.ToArray());
        }

        public int ToDocumentOffset(int sourceOffset)
        {
            var low = 0;
            var high = Breaks.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (Breaks[mid] <= sourceOffset) low = mid + 1; else high = mid;
            }
            return sourceOffset + low;
        }

        public int ToSourceOffset(int documentOffset)
        {
            var low = 0;
            var high = Breaks.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (Breaks[mid] + mid < documentOffset) low = mid + 1; else high = mid;
            }
            return Math.Clamp(documentOffset - low, 0, Source.Length);
        }
    }

    private sealed class PayloadColorizer(VirtualPayloadViewer viewer) : DocumentColorizingTransformer
    {
        private static readonly Regex Tokens = new("\"(?:\\\\.|[^\"\\\\])*\"\\s*:|\"(?:\\\\.|[^\"\\\\])*\"|-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?|\\b(?:true|false|null)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(30));
        private static readonly Brush Key = Frozen("#0A5C9C");
        private static readonly Brush String = Frozen("#167245");
        private static readonly Brush Number = Frozen("#8A4A00");
        private static readonly Brush Literal = Frozen("#8B2F75");
        private static readonly Brush Match = Frozen("#FFE79A");
        private static readonly Brush ActiveMatch = Frozen("#F2B84B");
        private static readonly Brush Added = Frozen("#DDF6DF");
        private static readonly Brush Removed = Frozen("#FDE2E2");

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line.Offset, line.Length);
            if (viewer.UseDiff && text.StartsWith("+ "))
                ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetBackgroundBrush(Added));
            else if (viewer.UseDiff && text.StartsWith("- "))
                ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetBackgroundBrush(Removed));
            try
            {
                foreach (Match token in Tokens.Matches(text))
                {
                    var brush = token.Value.EndsWith(':') ? Key : token.Value.StartsWith('"') ? String
                        : char.IsDigit(token.Value[0]) || token.Value[0] == '-' ? Number : Literal;
                    ChangeLinePart(line.Offset + token.Index, line.Offset + token.Index + token.Length,
                        element => element.TextRunProperties.SetForegroundBrush(brush));
                }
            }
            catch (RegexMatchTimeoutException) { }
            var low = 0;
            var high = viewer._matches.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (viewer._matches[mid].End <= line.Offset) low = mid + 1; else high = mid;
            }
            for (var i = low; i < viewer._matches.Length && viewer._matches[i].Start < line.EndOffset; i++)
            {
                var match = viewer._matches[i];
                var brush = i == viewer._activeMatch ? ActiveMatch : Match;
                ChangeLinePart(Math.Max(line.Offset, match.Start), Math.Min(line.EndOffset, match.End),
                    element => element.TextRunProperties.SetBackgroundBrush(brush));
            }
        }

        private static Brush Frozen(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            return brush;
        }
    }
}
