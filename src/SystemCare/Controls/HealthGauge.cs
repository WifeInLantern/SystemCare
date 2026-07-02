using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SystemCare.Helpers;

namespace SystemCare.Controls;

/// <summary>
/// Circular health-score gauge: a 270° track with a colored sweep, the score
/// centered, and the band label underneath. Score &lt; 0 renders as "not scanned".
/// </summary>
public class HealthGauge : FrameworkElement
{
    // Bundled Rajdhani (techy numerals) for the score readout.
    private static readonly FontFamily NumberFont =
        new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Rajdhani");

    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score), typeof(double), typeof(HealthGauge),
        new FrameworkPropertyMetadata(-1.0, OnScoreChanged));

    private static readonly DependencyProperty AnimatedScoreProperty = DependencyProperty.Register(
        nameof(AnimatedScore), typeof(double), typeof(HealthGauge),
        new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Optional replacement for the band caption under the score (e.g. a benchmark tier). When
    /// empty, the health band text (Excellent/Good/…) is used.</summary>
    public static readonly DependencyProperty BandLabelOverrideProperty = DependencyProperty.Register(
        nameof(BandLabelOverride), typeof(string), typeof(HealthGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public string? BandLabelOverride
    {
        get => (string?)GetValue(BandLabelOverrideProperty);
        set => SetValue(BandLabelOverrideProperty, value);
    }

    private double AnimatedScore => (double)GetValue(AnimatedScoreProperty);

    public HealthGauge()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Animations.ReduceMotionChanged -= OnReduceMotionChanged; // avoid a double subscription on reload
        Animations.ReduceMotionChanged += OnReduceMotionChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => Animations.ReduceMotionChanged -= OnReduceMotionChanged;

    // Reduce-motion toggled live: freeze the glow to a static band color, or resume the breathing pulse.
    private void OnReduceMotionChanged()
    {
        double score = Score;
        if (score < 0) return; // unscored: no glow either way
        if (Animations.ReduceMotion)
        {
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = BandColor(score), ShadowDepth = 0, BlurRadius = 24, Opacity = 0.45,
            };
        }
        else
        {
            UpdateGlow(score);
        }
    }

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
        if (Animations.ReduceMotion)
        {
            // Snap to the value and use a static band-colored glow (no count-up, no breathing).
            gauge.BeginAnimation(AnimatedScoreProperty, null);
            gauge.SetValue(AnimatedScoreProperty, target);
            gauge.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = BandColor(target), ShadowDepth = 0, BlurRadius = 24, Opacity = 0.45,
            };
            return;
        }
        double from = Math.Max(0, gauge.AnimatedScore);
        gauge.BeginAnimation(AnimatedScoreProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        gauge.UpdateGlow(target);
    }

    /// <summary>
    /// Soft band-colored glow behind the arc with a gentle looping pulse, plus a one-shot
    /// brightness flash so a freshly-computed score feels acknowledged.
    /// </summary>
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

        // Gentle forever-breathing pulse (started once the one-shot flash below completes).
        var breathe = new DoubleAnimation(0.35, 0.7, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        // One-shot "power-on" flash that hands off to the looping breathe.
        var flash = new DoubleAnimationUsingKeyFrames();
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        flash.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        flash.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        flash.Completed += (_, _) =>
        {
            if (ReferenceEquals(Effect, glow))
                glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, breathe);
        };
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, flash);
    }

    // Neon "Night City" bands: excellent = mint, good = cyan, attention = neon yellow, poor = magenta.
    // Resolved via CyberPalette so Theme.xaml edits propagate to the gauge.
    private static Color BandColor(double score) => score switch
    {
        >= 90 => CyberPalette.Success,
        >= 70 => CyberPalette.Accent,
        >= 40 => CyberPalette.Warning,
        _ => CyberPalette.Danger,
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

        // Track: a subtle glass gradient (top-lit) instead of a flat band.
        var trackBrush = new LinearGradientBrush(
            Color.FromRgb(0x16, 0x20, 0x2E), Color.FromRgb(0x0E, 0x16, 0x22), 90);
        trackBrush.Freeze();
        DrawArc(dc, center, radius, startAngle, sweepTotal,
            new Pen(trackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        // Faint tick marks around the sweep for a precision-instrument feel.
        DrawTicks(dc, center, radius, thickness, startAngle, sweepTotal);

        double score = AnimatedScore;
        bool hasScore = score >= 0;
        Color color = hasScore ? BandColor(score) : Color.FromRgb(0x44, 0x55, 0x6E);

        if (hasScore && score > 0.5)
        {
            double valueSweep = sweepTotal * Math.Clamp(score, 0, 100) / 100;

            // Value arc: band color blended into a cyan→magenta neon sheen.
            var arcBrush = new LinearGradientBrush(color, CyberPalette.Secondary, 45);
            DrawArc(dc, center, radius, startAngle, valueSweep,
                new Pen(arcBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

            // Glowing "head" dot at the leading tip of the value arc.
            DrawArcTip(dc, center, radius, thickness, startAngle + valueSweep, color);
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var scoreText = new FormattedText(
            hasScore ? Math.Round(score).ToString(CultureInfo.InvariantCulture) : "—",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(NumberFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size * 0.30, new SolidColorBrush(color), dpi);
        dc.DrawText(scoreText, new Point(center.X - scoreText.Width / 2, center.Y - scoreText.Height * 0.62));

        string caption = hasScore
            ? (string.IsNullOrEmpty(BandLabelOverride) ? BandText(score) : BandLabelOverride!).ToUpperInvariant()
            : "NOT SCANNED YET";
        var label = new FormattedText(
            caption,
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(NumberFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size * 0.06,
            new SolidColorBrush(CyberPalette.TextSecondary), dpi);
        dc.DrawText(label, new Point(center.X - label.Width / 2, center.Y + scoreText.Height * 0.42));
    }

    /// <summary>Faint radial ticks evenly spaced along the 270° sweep.</summary>
    private static void DrawTicks(DrawingContext dc, Point center, double radius, double thickness,
        double startDeg, double sweepDeg)
    {
        const int count = 10;
        var accent = CyberPalette.Accent;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)), 1.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();

        double inner = radius - thickness * 0.5 - 3;
        double outer = radius - thickness * 0.5 - 3 + Math.Max(3, thickness * 0.35);
        for (int i = 0; i <= count; i++)
        {
            double rad = (startDeg + sweepDeg * i / count) * Math.PI / 180;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            dc.DrawLine(pen,
                new Point(center.X + inner * cos, center.Y + inner * sin),
                new Point(center.X + outer * cos, center.Y + outer * sin));
        }
    }

    /// <summary>A bright dot with a soft halo at the leading edge of the value arc.</summary>
    private static void DrawArcTip(DrawingContext dc, Point center, double radius, double thickness,
        double tipDeg, Color color)
    {
        double rad = tipDeg * Math.PI / 180;
        var tip = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));

        // Soft halo.
        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, color.R, color.G, color.B), 0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1));
        halo.Freeze();
        dc.DrawEllipse(halo, null, tip, thickness * 1.4, thickness * 1.4);

        // Crisp core.
        var core = new SolidColorBrush(Color.FromRgb(0xF0, 0xFB, 0xFF));
        core.Freeze();
        dc.DrawEllipse(core, null, tip, thickness * 0.32, thickness * 0.32);
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
