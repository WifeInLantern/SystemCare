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

        // Sample points across the width.
        var pts = new Point[n];
        for (int i = 0; i < n; i++) pts[i] = new Point(i * step, Y(values[i]));

        // Build a smooth curve through the points using Catmull-Rom -> cubic Bezier segments,
        // so the live graph flows instead of showing hard polyline kinks.
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            lc.BeginFigure(pts[0], false, false);
            fc.BeginFigure(new Point(0, h), true, true);
            fc.LineTo(pts[0], false, false);

            for (int i = 0; i < n - 1; i++)
            {
                Point p0 = pts[i == 0 ? 0 : i - 1];
                Point p1 = pts[i];
                Point p2 = pts[i + 1];
                Point p3 = pts[i + 2 < n ? i + 2 : n - 1];

                // Catmull-Rom -> Bezier control points (tension 1/6).
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

                lc.BezierTo(c1, c2, p2, true, false);
                fc.BezierTo(c1, c2, p2, true, false);
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

        // Neon glow: a wider, translucent underlay stroke behind the crisp line.
        var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, Accent.R, Accent.G, Accent.B)), 5)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        glowPen.Freeze();
        dc.DrawGeometry(null, glowPen, line);
        dc.DrawGeometry(null, pen, line);

        // Glowing head dot at the latest sample — the live focal point.
        var head = pts[n - 1];
        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, Accent.R, Accent.G, Accent.B), 0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, Accent.R, Accent.G, Accent.B), 1));
        halo.Freeze();
        dc.DrawEllipse(halo, null, head, 7, 7);

        var core = new SolidColorBrush(Color.FromRgb(0xF0, 0xFB, 0xFF));
        core.Freeze();
        dc.DrawEllipse(core, null, head, 2.2, 2.2);
    }
}
