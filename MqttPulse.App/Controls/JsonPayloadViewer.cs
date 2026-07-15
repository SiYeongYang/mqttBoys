using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MqttPulse.App.Controls;

public sealed class JsonPayloadViewer : RichTextBox
{
    private const int HighlightLimit = 250_000;
    private static readonly Brush TextBrush = Frozen("#17211F");
    private static readonly Brush KeyBrush = Frozen("#0A5C9C");
    private static readonly Brush StringBrush = Frozen("#167245");
    private static readonly Brush NumberBrush = Frozen("#8A4A00");
    private static readonly Brush LiteralBrush = Frozen("#8B2F75");
    private static readonly Brush PunctuationBrush = Frozen("#65716E");
    private bool _rendering;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(JsonPayloadViewer),
        new FrameworkPropertyMetadata(string.Empty, OnTextChanged));

    public JsonPayloadViewer()
    {
        IsReadOnly = true;
        IsDocumentEnabled = false;
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

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;

        if (e.Delta == 0 || ExtentHeight <= ViewportHeight)
        {
            return;
        }

        var direction = e.Delta > 0 ? -1 : 1;
        var wheelNotches = Math.Max(1.0, Math.Abs(e.Delta) / 120.0);
        var step = Math.Max(FontSize * 3.0, 36.0) * wheelNotches;
        var maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        var targetOffset = Math.Clamp(VerticalOffset + (direction * step), 0, maxOffset);
        ScrollToVerticalOffset(targetOffset);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonPayloadViewer viewer)
        {
            viewer.Render((string?)e.NewValue ?? string.Empty);
        }
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
            Document = CreateDocument(text);
        }
        finally
        {
            _rendering = false;
        }

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

    private bool IsNearBottom()
    {
        return ExtentHeight <= 0 || VerticalOffset + ViewportHeight >= ExtentHeight - 2;
    }

    private FlowDocument CreateDocument(string text)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            FontFamily = FontFamily,
            FontSize = FontSize
        };

        if (LooksLikeJson(text) && text.Length <= HighlightLimit)
        {
            AddJsonRuns(paragraph, text);
        }
        else
        {
            paragraph.Inlines.Add(new Run(text) { Foreground = TextBrush });
        }

        return new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(8, 4, 8, 4),
            FontFamily = FontFamily,
            FontSize = FontSize,
            PageWidth = 4096
        };
    }

    private static void AddJsonRuns(Paragraph paragraph, string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                var start = index;
                index = ReadString(text, index);
                AddRun(paragraph, text, start, index, IsKey(text, index) ? KeyBrush : StringBrush);
                continue;
            }

            if (ch == '-' || char.IsDigit(ch))
            {
                var start = index;
                index = ReadNumber(text, index);
                AddRun(paragraph, text, start, index, NumberBrush);
                continue;
            }

            if (StartsWithLiteral(text, index, "true") || StartsWithLiteral(text, index, "false") || StartsWithLiteral(text, index, "null"))
            {
                var start = index;
                index += text[index] == 't' || text[index] == 'n' ? 4 : 5;
                AddRun(paragraph, text, start, index, LiteralBrush);
                continue;
            }

            if (IsPunctuation(ch))
            {
                AddRun(paragraph, text, index, index + 1, PunctuationBrush);
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

            AddRun(paragraph, text, plainStart, index, TextBrush);
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

    private static void AddRun(Paragraph paragraph, string text, int start, int end, Brush brush)
    {
        if (end <= start)
        {
            return;
        }

        paragraph.Inlines.Add(new Run(text[start..end]) { Foreground = brush });
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}