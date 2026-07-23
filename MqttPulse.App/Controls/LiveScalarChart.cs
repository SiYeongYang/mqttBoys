using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MqttPulse.App.Models;

namespace MqttPulse.App.Controls;

public sealed class LiveScalarChart : FrameworkElement
{
    private static readonly Brush GridBrush = FrozenBrush("#DCE5E3");
    private static readonly Brush AxisTextBrush = FrozenBrush("#536763");
    private static readonly Brush HoverBrush = FrozenBrush("#3F5652");
    private static readonly Brush DefaultSeriesBrush = FrozenBrush("#F59E0B");
    private static readonly Pen GridPen = FrozenPen(GridBrush, 1);
    private static readonly Pen HoverPen = CreateHoverPen();
    private static readonly Pen WhiteOutlinePen = FrozenPen(Brushes.White, 1.5);
    private static readonly Typeface AxisTypeface = new("Segoe UI");
    private readonly ToolTip _pointToolTip;
    private Pen _seriesPen = CreateSeriesPen(DefaultSeriesBrush);
    private int _hoverIndex = -1;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IReadOnlyList<ChartPoint>),
        typeof(LiveScalarChart),
        new FrameworkPropertyMetadata(
            Array.Empty<ChartPoint>(),
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnPointsChanged));

    public static readonly DependencyProperty IsBooleanProperty = DependencyProperty.Register(
        nameof(IsBoolean),
        typeof(bool),
        typeof(LiveScalarChart),
        new FrameworkPropertyMetadata(
            false,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeriesBrushProperty = DependencyProperty.Register(
        nameof(SeriesBrush),
        typeof(Brush),
        typeof(LiveScalarChart),
        new FrameworkPropertyMetadata(
            DefaultSeriesBrush,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnSeriesBrushChanged));

    public LiveScalarChart()
    {
        ClipToBounds = true;
        Focusable = false;
        _pointToolTip = new ToolTip
        {
            Placement = PlacementMode.Mouse,
            PlacementTarget = this,
            StaysOpen = true
        };
        Unloaded += (_, _) => _pointToolTip.IsOpen = false;
    }

    public IReadOnlyList<ChartPoint> Points
    {
        get => (IReadOnlyList<ChartPoint>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public bool IsBoolean
    {
        get => (bool)GetValue(IsBooleanProperty);
        set => SetValue(IsBooleanProperty, value);
    }

    public Brush SeriesBrush
    {
        get => (Brush)GetValue(SeriesBrushProperty);
        set => SetValue(SeriesBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var chartRect = GetChartRect();
        if (chartRect.Width <= 40 || chartRect.Height <= 40)
        {
            return;
        }

        var points = Points ?? Array.Empty<ChartPoint>();
        if (points.Count == 0)
        {
            DrawCenteredText(drawingContext, "Waiting for values", chartRect);
            return;
        }

        GetValueRange(points, out var minimum, out var maximum);
        DrawGrid(drawingContext, chartRect, minimum, maximum);
        DrawSeries(drawingContext, chartRect, points, minimum, maximum);
        DrawTimeLabels(drawingContext, chartRect, points);

        if (_hoverIndex >= 0 && _hoverIndex < points.Count)
        {
            var hoverPoint = ToPoint(chartRect, points, _hoverIndex, minimum, maximum);
            drawingContext.DrawLine(
                HoverPen,
                new Point(hoverPoint.X, chartRect.Top),
                new Point(hoverPoint.X, chartRect.Bottom));
            drawingContext.DrawEllipse(SeriesBrush, null, hoverPoint, 5, 5);
            drawingContext.DrawEllipse(null, WhiteOutlinePen, hoverPoint, 5, 5);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var chartRect = GetChartRect();
        var position = e.GetPosition(this);
        var points = Points ?? Array.Empty<ChartPoint>();
        if (!chartRect.Contains(position) || points.Count == 0)
        {
            ClosePointToolTip();
            return;
        }

        var nearest = FindNearestPoint(points, chartRect, position.X);
        if (nearest < 0)
        {
            ClosePointToolTip();
            return;
        }

        _hoverIndex = nearest;
        var point = points[nearest];
        _pointToolTip.Content =
            $"Time   {point.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}\n"
            + $"Value  {FormatValue(point.Value)}";
        _pointToolTip.IsOpen = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClosePointToolTip();
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LiveScalarChart chart)
        {
            chart._hoverIndex = -1;
            chart._pointToolTip.IsOpen = false;
        }
    }

    private static void OnSeriesBrushChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is LiveScalarChart chart && e.NewValue is Brush brush)
        {
            chart._seriesPen = CreateSeriesPen(brush);
        }
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
            drawingContext.DrawLine(
                GridPen,
                new Point(chartRect.Left, y),
                new Point(chartRect.Right, y));

            var value = minimum + ((maximum - minimum) * ratio);
            var label = IsBoolean
                ? (value >= 0.5 ? "True" : "False")
                : FormatAxisValue(value);
            DrawText(
                drawingContext,
                label,
                new Point(chartRect.Left - 8, y),
                alignRight: true,
                centerVertically: true);
        }
    }

    private void DrawSeries(
        DrawingContext drawingContext,
        Rect chartRect,
        IReadOnlyList<ChartPoint> points,
        double minimum,
        double maximum)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < points.Count; index++)
            {
                var point = ToPoint(chartRect, points, index, minimum, maximum);
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
        drawingContext.DrawGeometry(null, _seriesPen, geometry);
        foreach (var point in points.Select((_, index) =>
                     ToPoint(chartRect, points, index, minimum, maximum)))
        {
            drawingContext.DrawEllipse(SeriesBrush, null, point, 2.75, 2.75);
        }
    }

    private void DrawTimeLabels(
        DrawingContext drawingContext,
        Rect chartRect,
        IReadOnlyList<ChartPoint> points)
    {
        DrawText(
            drawingContext,
            points[0].Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            new Point(chartRect.Left, chartRect.Bottom + 8));
        DrawText(
            drawingContext,
            points[^1].Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            new Point(chartRect.Right, chartRect.Bottom + 8),
            alignRight: true);
    }

    private void GetValueRange(
        IReadOnlyList<ChartPoint> points,
        out double minimum,
        out double maximum)
    {
        if (IsBoolean)
        {
            minimum = 0;
            maximum = 1;
            return;
        }

        minimum = points.Min(point => point.Value);
        maximum = points.Max(point => point.Value);
        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            var padding = Math.Max(1, Math.Abs(maximum) * 0.05);
            minimum -= padding;
            maximum += padding;
            return;
        }

        var rangePadding = (maximum - minimum) * 0.1;
        minimum -= rangePadding;
        maximum += rangePadding;
    }

    private static Point ToPoint(
        Rect chartRect,
        IReadOnlyList<ChartPoint> points,
        int index,
        double minimum,
        double maximum)
    {
        var startTicks = points[0].Timestamp.UtcTicks;
        var endTicks = points[^1].Timestamp.UtcTicks;
        var xRatio = endTicks == startTicks
            ? (points.Count <= 1 ? 0.5 : (double)index / (points.Count - 1))
            : (double)(points[index].Timestamp.UtcTicks - startTicks) / (endTicks - startTicks);
        var yRatio = (points[index].Value - minimum) / (maximum - minimum);
        return new Point(
            chartRect.Left + (chartRect.Width * xRatio),
            chartRect.Bottom - (chartRect.Height * yRatio));
    }

    private static int FindNearestPoint(
        IReadOnlyList<ChartPoint> points,
        Rect chartRect,
        double mouseX)
    {
        var startTicks = points[0].Timestamp.UtcTicks;
        var endTicks = points[^1].Timestamp.UtcTicks;
        var bestIndex = -1;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < points.Count; index++)
        {
            var ratio = endTicks == startTicks
                ? (points.Count <= 1 ? 0.5 : (double)index / (points.Count - 1))
                : (double)(points[index].Timestamp.UtcTicks - startTicks) / (endTicks - startTicks);
            var x = chartRect.Left + (chartRect.Width * ratio);
            var distance = Math.Abs(x - mouseX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private Rect GetChartRect() =>
        new(52, 10, Math.Max(1, ActualWidth - 66), Math.Max(1, ActualHeight - 40));

    private void ClosePointToolTip()
    {
        _hoverIndex = -1;
        _pointToolTip.IsOpen = false;
        InvalidateVisual();
    }

    private string FormatValue(double value)
    {
        if (IsBoolean)
        {
            return value >= 0.5 ? "true" : "false";
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
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
            10.5,
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

    private static Pen CreateHoverPen()
    {
        var pen = new Pen(HoverBrush, 1)
        {
            DashStyle = DashStyles.Dash
        };
        pen.Freeze();
        return pen;
    }

    private static Pen CreateSeriesPen(Brush brush)
    {
        var pen = new Pen(brush, 1.75);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }
}
