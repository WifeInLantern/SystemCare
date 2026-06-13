using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SystemCare.Controls;

/// <summary>
/// Circular health-score gauge: a 270° track with a colored sweep, the score
/// centered, and the band label underneath. Score &lt; 0 renders as "not scanned".
/// </summary>
public class HealthGauge : FrameworkElement
{
    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score), typeof(double), typeof(HealthGauge),
        new FrameworkPropertyMetadata(-1.0, OnScoreChanged));

    private static readonly DependencyProperty AnimatedScoreProperty = DependencyProperty.Register(
        nameof(AnimatedScore), typeof(double), typeof(HealthGauge),
        new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    private double AnimatedScore => (double)GetValue(AnimatedScoreProperty);

    private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (HealthGauge)d;
        double target = (double)e.NewValue;
        if (target < 0)
        {
            gauge.BeginAnimation(AnimatedScoreProperty, null);
            gauge.SetValue(AnimatedScoreProperty, -1.0);
            gauge.Effect = null;
            return;
        }
        double from = Math.Max(0, gauge.AnimatedScore);
        gauge.BeginAnimation(AnimatedScoreProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        gauge.UpdateGlow(target);
    }

    /// <summary>Soft band-colored glow behind the arc with a gentle looping pulse.</summary>
    private void UpdateGlow(double score)
    {
        var glow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = BandColor(score),
            ShadowDepth = 0,
            BlurRadius = 30,
            Opacity = 0.5,
        };
        Effect = glow;
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(1.6))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    private static Color BandColor(double score) => score switch
    {
        >= 90 => Color.FromRgb(0x4C, 0xAF, 0x50),
        >= 70 => Color.FromRgb(0x8B, 0xC3, 0x4A),
        >= 40 => Color.FromRgb(0xFF, 0x98, 0x00),
        _ => Color.FromRgb(0xF4, 0x43, 0x36),
    };

    private static string BandText(double score) => score switch
    {
        >= 90 => "Excellent",
        >= 70 => "Good",
        >= 40 => "Needs attention",
        _ => "Poor",
    };

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width < 40 || height < 40) return;

        double size = Math.Min(width, height);
        double thickness = size * 0.075;
        double radius = (size - thickness) / 2 - 2;
        var center = new Point(width / 2, height / 2);

        const double startAngle = 135;   // gauge opens downward
        const double sweepTotal = 270;

        // Track
        DrawArc(dc, center, radius, startAngle, sweepTotal,
            new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x37, 0x3B)), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        double score = AnimatedScore;
        bool hasScore = score >= 0;
        Color color = hasScore ? BandColor(score) : Color.FromRgb(0x66, 0x6A, 0x6E);

        if (hasScore && score > 0.5)
        {
            DrawArc(dc, center, radius, startAngle, sweepTotal * Math.Clamp(score, 0, 100) / 100,
                new Pen(new SolidColorBrush(color), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var scoreText = new FormattedText(
            hasScore ? Math.Round(score).ToString(CultureInfo.InvariantCulture) : "—",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size * 0.26, new SolidColorBrush(color), dpi);
        dc.DrawText(scoreText, new Point(center.X - scoreText.Width / 2, center.Y - scoreText.Height * 0.62));

        var label = new FormattedText(
            hasScore ? BandText(score) : "Not scanned yet",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size * 0.055,
            new SolidColorBrush(Color.FromRgb(0xB0, 0xB4, 0xB8)), dpi);
        dc.DrawText(label, new Point(center.X - label.Width / 2, center.Y + scoreText.Height * 0.42));
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double startDeg, double sweepDeg, Pen pen)
    {
        if (sweepDeg <= 0) return;
        sweepDeg = Math.Min(sweepDeg, 359.99);

        Point PointAt(double deg)
        {
            double rad = deg * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointAt(startDeg), false, false);
            ctx.ArcTo(PointAt(startDeg + sweepDeg), new Size(radius, radius), 0,
                sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
