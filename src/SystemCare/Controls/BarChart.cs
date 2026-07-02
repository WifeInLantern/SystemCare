using System.Windows;
using System.Windows.Media;

namespace SystemCare.Controls;

/// <summary>
/// Lightweight custom-drawn bar chart in the same visual family as <see cref="SparklineChart"/>:
/// rounded accent-coloured bars with a soft glow, no axes (labels live in the surrounding XAML).
/// Used by the Care Report's "space freed" charts.
/// </summary>
public class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(BarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(
        nameof(Max), typeof(double), typeof(BarChart),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(BarChart),
        new FrameworkPropertyMetadata(Color.FromRgb(0x00, 0xE5, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender));

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
        if (values is null || values.Count == 0) return;

        double max = Max <= 0 ? 1 : Max;
        int n = values.Count;
        double slot = w / n;
        double gap = Math.Min(4, slot * 0.25);
        double barWidth = Math.Max(1, slot - gap);
        double radius = Math.Min(2.5, barWidth / 2);

        // Vertical gradient fill echoing the sparkline's treatment, plus a faint full-height
        // track behind each bar so zero days still read as part of the series.
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, Accent.R, Accent.G, Accent.B), 0));
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, Accent.R, Accent.G, Accent.B), 1));
        fill.Freeze();

        var glow = new SolidColorBrush(Color.FromArgb(0x2E, Accent.R, Accent.G, Accent.B));
        glow.Freeze();

        var track = new SolidColorBrush(Color.FromArgb(0x14, Accent.R, Accent.G, Accent.B));
        track.Freeze();

        for (int i = 0; i < n; i++)
        {
            double x = i * slot + gap / 2;
            dc.DrawRoundedRectangle(track, null, new Rect(x, 0, barWidth, h), radius, radius);

            double v = Math.Clamp(values[i] / max, 0, 1);
            if (v <= 0) continue;

            double barHeight = Math.Max(2, v * h);
            var rect = new Rect(x, h - barHeight, barWidth, barHeight);
            var glowRect = Rect.Inflate(rect, 1.5, 1.5);
            dc.DrawRoundedRectangle(glow, null, glowRect, radius + 1.5, radius + 1.5);
            dc.DrawRoundedRectangle(fill, null, rect, radius, radius);
        }
    }
}
