using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MqttPulse.App.Controls;

public sealed class ScalarHistoryChart : FrameworkElement
{
    private static readonly Brush GridBrush = FrozenBrush("#DCE5E3");
    private static readonly Brush AxisTextBrush = FrozenBrush("#536763");
    private static readonly Brush LineBrush = FrozenBrush("#176E63");
    private static readonly Brush PointBrush = FrozenBrush("#105B52");
    private static readonly Pen GridPen = FrozenPen(GridBrush, 1);
    private static readonly Pen LinePen = FrozenPen(LineBrush, 2);
    private static readonly Typeface AxisTypeface = new("Segoe UI");

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IReadOnlyList<double>),
        typeof(ScalarHistoryChart),
        new FrameworkPropertyMetadata(
            Array.Empty<double>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsBooleanProperty = DependencyProperty.Register(
        nameof(IsBoolean),
        typeof(bool),
        typeof(ScalarHistoryChart),
        new FrameworkPropertyMetadata(
            false,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public bool IsBoolean
    {
        get => (bool)GetValue(IsBooleanProperty);
        set => SetValue(IsBooleanProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 100 || height <= 80)
        {
            return;
        }

        var chartRect = new Rect(54, 12, Math.Max(1, width - 70), Math.Max(1, height - 44));
        var values = Values ?? Array.Empty<double>();
        if (values.Count == 0)
        {
            DrawCenteredText(drawingContext, "No numeric or boolean samples", chartRect);
            return;
        }

        var minimum = IsBoolean ? 0 : values.Min();
        var maximum = IsBoolean ? 1 : values.Max();
        if (!IsBoolean)
        {
            if (Math.Abs(maximum - minimum) < double.Epsilon)
            {
                var padding = Math.Max(1, Math.Abs(maximum) * 0.05);
                minimum -= padding;
                maximum += padding;
            }
            else
            {
                var padding = (maximum - minimum) * 0.08;
                minimum -= padding;
                maximum += padding;
            }
        }

        DrawGrid(drawingContext, chartRect, minimum, maximum);
        DrawSeries(drawingContext, chartRect, values, minimum, maximum);
        DrawXAxisLabels(drawingContext, chartRect);
    }

    private void DrawGrid(
        DrawingContext drawingContext,
        Rect chartRect,
        double minimum,
        double maximum)
    {
        var gridCount = IsBoolean ? 2 : 5;
        for (var index = 0; index < gridCount; index++)
        {
            var ratio = gridCount == 1 ? 0 : (double)index / (gridCount - 1);
            var y = chartRect.Bottom - (chartRect.Height * ratio);
            drawingContext.DrawLine(GridPen, new Point(chartRect.Left, y), new Point(chartRect.Right, y));

            var value = minimum + ((maximum - minimum) * ratio);
            var label = IsBoolean
                ? (value >= 0.5 ? "True" : "False")
                : FormatAxisValue(value);
            DrawText(drawingContext, label, new Point(chartRect.Left - 8, y), alignRight: true, centerVertically: true);
        }
    }

    private static void DrawSeries(
        DrawingContext drawingContext,
        Rect chartRect,
        IReadOnlyList<double> values,
        double minimum,
        double maximum)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < values.Count; index++)
            {
                var point = ToPoint(chartRect, values, index, minimum, maximum);
                if (index == 0)
                {
                    context.BeginFigure(point, isFilled: false, isClosed: false);
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: true);
                }
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, LinePen, geometry);

        if (values.Count <= 40)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var point = ToPoint(chartRect, values, index, minimum, maximum);
                drawingContext.DrawEllipse(PointBrush, null, point, 2.5, 2.5);
            }
        }
    }

    private static Point ToPoint(
        Rect chartRect,
        IReadOnlyList<double> values,
        int index,
        double minimum,
        double maximum)
    {
        var xRatio = values.Count <= 1 ? 0.5 : (double)index / (values.Count - 1);
        var yRatio = (values[index] - minimum) / (maximum - minimum);
        return new Point(
            chartRect.Left + (chartRect.Width * xRatio),
            chartRect.Bottom - (chartRect.Height * yRatio));
    }

    private void DrawXAxisLabels(DrawingContext drawingContext, Rect chartRect)
    {
        DrawText(drawingContext, "oldest", new Point(chartRect.Left, chartRect.Bottom + 8));
        DrawText(drawingContext, "latest", new Point(chartRect.Right, chartRect.Bottom + 8), alignRight: true);
    }

    private void DrawCenteredText(DrawingContext drawingContext, string text, Rect rect)
    {
        var formatted = CreateText(text);
        drawingContext.DrawText(
            formatted,
            new Point(
                rect.Left + ((rect.Width - formatted.Width) / 2),
                rect.Top + ((rect.Height - formatted.Height) / 2)));
    }

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        Point anchor,
        bool alignRight = false,
        bool centerVertically = false)
    {
        var formatted = CreateText(text);
        var x = alignRight ? anchor.X - formatted.Width : anchor.X;
        var y = centerVertically ? anchor.Y - (formatted.Height / 2) : anchor.Y;
        drawingContext.DrawText(formatted, new Point(x, y));
    }

    private FormattedText CreateText(string text) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            AxisTypeface,
            11,
            AxisTextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static string FormatAxisValue(double value)
    {
        var absolute = Math.Abs(value);
        if ((absolute > 0 && absolute < 0.01) || absolute >= 1_000_000)
        {
            return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static SolidColorBrush FrozenBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
