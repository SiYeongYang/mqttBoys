using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MqttPulse.Core;

namespace MqttPulse.App.Controls;

public sealed class JsonDiffViewer : RichTextBox
{
    private static readonly Brush TextBrush = Frozen("#17211F");
    private static readonly Brush MutedBrush = Frozen("#65716E");
    private static readonly Brush AddedBrush = Frozen("#167245");
    private static readonly Brush AddedBackgroundBrush = Frozen("#DDF6DF");
    private static readonly Brush RemovedBrush = Frozen("#A62E2E");
    private static readonly Brush RemovedBackgroundBrush = Frozen("#FDE2E2");
    private CancellationTokenSource? _renderCancellation;

    public static readonly DependencyProperty BaselineTextProperty = DependencyProperty.Register(
        nameof(BaselineText),
        typeof(string),
        typeof(JsonDiffViewer),
        new FrameworkPropertyMetadata(string.Empty, OnDiffInputChanged));

    public static readonly DependencyProperty CurrentTextProperty = DependencyProperty.Register(
        nameof(CurrentText),
        typeof(string),
        typeof(JsonDiffViewer),
        new FrameworkPropertyMetadata(string.Empty, OnDiffInputChanged));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(JsonDiffViewer),
        new FrameworkPropertyMetadata(false, OnDiffInputChanged));

    public JsonDiffViewer()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        UndoLimit = 0;
        BorderThickness = new Thickness(1);
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Document = CreateDocument();
        RenderMessage("Select a History message to compare.");
    }

    public string BaselineText
    {
        get => (string)GetValue(BaselineTextProperty);
        set => SetValue(BaselineTextProperty, value);
    }

    public string CurrentText
    {
        get => (string)GetValue(CurrentTextProperty);
        set => SetValue(CurrentTextProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
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
            if (ExtentWidth > ViewportWidth)
            {
                var maxOffset = Math.Max(0, ExtentWidth - ViewportWidth);
                ScrollToHorizontalOffset(Math.Clamp(HorizontalOffset + (direction * step), 0, maxOffset));
            }

            return;
        }

        if (ExtentHeight > ViewportHeight)
        {
            var maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
            ScrollToVerticalOffset(Math.Clamp(VerticalOffset + (direction * step), 0, maxOffset));
        }
    }

    private static void OnDiffInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonDiffViewer viewer)
        {
            viewer.QueueRender();
        }
    }

    private void QueueRender()
    {
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = null;

        if (!IsActive)
        {
            return;
        }

        if (string.IsNullOrEmpty(BaselineText))
        {
            RenderMessage("Select a History message to compare.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _renderCancellation = cancellation;
        _ = RenderAsync(BaselineText, CurrentText, cancellation.Token);
    }

    private async Task RenderAsync(string baseline, string current, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(80, cancellationToken);
            var result = await Task.Run(
                () => JsonLineDiffer.Compare(baseline, current),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(
                () => RenderResult(result),
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RenderResult(JsonLineDiffResult result)
    {
        var verticalOffset = VerticalOffset;
        var horizontalOffset = HorizontalOffset;
        var wasAtBottom = IsNearBottom();
        var document = CreateDocument();

        foreach (var line in result.Lines)
        {
            document.Blocks.Add(CreateLine(line));
        }

        var summary = new Paragraph
        {
            Margin = new Thickness(0, 8, 0, 0),
            FontFamily = FontFamily,
            FontSize = FontSize,
            TextAlignment = TextAlignment.Left
        };
        summary.Inlines.Add(new Run("Comparing with selected message: ") { Foreground = MutedBrush });
        summary.Inlines.Add(new Run($"+ {result.AddedLineCount} lines") { Foreground = AddedBrush });
        summary.Inlines.Add(new Run(", "));
        summary.Inlines.Add(new Run($"- {result.RemovedLineCount} lines") { Foreground = RemovedBrush });
        if (result.IsSimplified)
        {
            summary.Inlines.Add(new Run(" (large comparison simplified)") { Foreground = MutedBrush });
        }

        document.Blocks.Add(summary);
        Document = document;
        RestoreScroll(verticalOffset, horizontalOffset, wasAtBottom);
    }

    private void RenderMessage(string message)
    {
        var document = CreateDocument();
        document.Blocks.Add(new Paragraph(new Run(message) { Foreground = MutedBrush })
        {
            Margin = new Thickness(0),
            FontFamily = FontFamily,
            FontSize = FontSize
        });
        Document = document;
    }

    private Paragraph CreateLine(JsonDiffLine line)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0),
            FontFamily = FontFamily,
            FontSize = FontSize,
            Background = line.Kind switch
            {
                JsonDiffKind.Added => AddedBackgroundBrush,
                JsonDiffKind.Removed => RemovedBackgroundBrush,
                _ => Brushes.Transparent
            }
        };

        var prefix = line.Kind switch
        {
            JsonDiffKind.Added => "+ ",
            JsonDiffKind.Removed => "- ",
            _ => "  "
        };
        var brush = line.Kind switch
        {
            JsonDiffKind.Added => AddedBrush,
            JsonDiffKind.Removed => RemovedBrush,
            _ => TextBrush
        };
        paragraph.Inlines.Add(new Run(prefix) { Foreground = brush, FontWeight = FontWeights.SemiBold });
        paragraph.Inlines.Add(new Run(line.Text) { Foreground = brush });
        return paragraph;
    }

    private FlowDocument CreateDocument()
    {
        return new FlowDocument
        {
            PagePadding = new Thickness(8, 4, 8, 4),
            FontFamily = FontFamily,
            FontSize = FontSize,
            PageWidth = 4096
        };
    }

    private bool IsNearBottom()
    {
        return ExtentHeight <= 0 || VerticalOffset + ViewportHeight >= ExtentHeight - 2;
    }

    private void RestoreScroll(double verticalOffset, double horizontalOffset, bool wasAtBottom)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (wasAtBottom)
            {
                ScrollToEnd();
                return;
            }

            ScrollToVerticalOffset(Math.Max(0, verticalOffset));
            ScrollToHorizontalOffset(Math.Max(0, horizontalOffset));
        });
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
