using System.Windows;
using System.Windows.Media;

namespace SystemCare.Controls;

/// <summary>
/// Lightweight rolling line chart: feed it a rolling buffer of values via
/// <see cref="Values"/> and it draws a smooth accent-colored line with a soft
/// gradient fill. Used on the dashboard cards and the System Info page.
/// </summary>
public class SparklineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(SparklineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(
        nameof(Max), typeof(double), typeof(SparklineChart),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(SparklineChart),
        new FrameworkPropertyMetadata(Color.FromRgb(0x21, 0x96, 0xF3), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Max
    {
        get => (double)GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        var values = Values;
        if (values is null || values.Count < 2) return;

        double max = Max <= 0 ? 1 : Max;
        int n = values.Count;
        double step = w / (n - 1);

        double Y(double v) => h - Math.Clamp(v / max, 0, 1) * (h - 2) - 1;

        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            var first = new Point(0, Y(values[0]));
            lc.BeginFigure(first, false, false);
            fc.BeginFigure(new Point(0, h), true, true);
            fc.LineTo(first, false, false);

            for (int i = 1; i < n; i++)
            {
                var p = new Point(i * step, Y(values[i]));
                lc.LineTo(p, true, true);
                fc.LineTo(p, true, false);
            }
            fc.LineTo(new Point(w, h), false, false);
        }
        line.Freeze();
        fill.Freeze();

        var fillBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, Accent.R, Accent.G, Accent.B), 0));
        fillBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 1));
        fillBrush.Freeze();

        dc.DrawGeometry(fillBrush, null, fill);

        var pen = new Pen(new SolidColorBrush(Accent), 2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}
