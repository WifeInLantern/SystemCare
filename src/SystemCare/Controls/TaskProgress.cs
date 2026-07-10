using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SystemCare.Controls;

/// <summary>
/// Task loading indicator: a neon pill filled with blue-to-cyan vertical tick segments that light up
/// left-to-right, with a percentage at the end. Replaces the old indeterminate ProgressBar.
/// Drive it with <see cref="IsActive"/> (the task's busy flag). When no real progress is available the
/// fill is simulated - it eases toward a ceiling (&lt;100%) while busy and only reaches 100% when the
/// task finishes. Bind <see cref="Value"/> (0-100) when a real percentage exists.
/// </summary>
public class TaskProgress : FrameworkElement
{
    private const double Ceiling = 92;      // simulated progress never passes this while busy
    private const double RampSeconds = 9;   // asymptotic climb toward the ceiling
    private const double HoldSeconds = 0.8; // keep the full bar up after completion

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(TaskProgress), new PropertyMetadata(false, OnIsActiveChanged));
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TaskProgress), new PropertyMetadata(double.NaN, OnValueChanged));
    /// <summary>Real progress 0-100; leave NaN to simulate.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    private static readonly DependencyProperty DisplayProgressProperty = DependencyProperty.Register(
        nameof(DisplayProgress), typeof(double), typeof(TaskProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private double DisplayProgress { get => (double)GetValue(DisplayProgressProperty); set => SetValue(DisplayProgressProperty, value); }

    private static readonly DependencyProperty PulseProperty = DependencyProperty.Register(
        nameof(Pulse), typeof(double), typeof(TaskProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }

    private static readonly Color DeepBlue = Color.FromRgb(0x1E, 0x50, 0xC8);
    private static readonly Color HeadColor = Color.FromRgb(0xC8, 0xFB, 0xFF);
    private static readonly Color Unlit = Color.FromRgb(0x18, 0x32, 0x6E);

    // Theme accent resolved via CyberPalette so a Theme.xaml edit propagates here.
    private readonly Color _accent = Helpers.CyberPalette.Accent;
    private readonly Brush _track = new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x30));
    private readonly Pen _border;
    private readonly Brush _unlit = new SolidColorBrush(Unlit) { Opacity = 0.32 };
    private readonly Brush _label;
    private readonly Typeface _num = new(new FontFamily("pack://application:,,,/Assets/Fonts/#Rajdhani"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public TaskProgress()
    {
        Height = 22;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Visibility = Visibility.Collapsed;
        Effect = new DropShadowEffect { Color = _accent, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.5 };
        _border = new Pen(new SolidColorBrush(_accent), 1.4);
        _label = new SolidColorBrush(_accent);
        _track.Freeze();
        _border.Freeze();
        _unlit.Freeze();
        _label.Freeze();

        // Live Reduce-motion toggles freeze or restart the busy pulse in place, like HealthGauge.
        Loaded += (_, _) =>
        {
            Helpers.Animations.ReduceMotionChanged -= OnReduceMotionChanged; // avoid a double subscription on reload
            Helpers.Animations.ReduceMotionChanged += OnReduceMotionChanged;
        };
        Unloaded += (_, _) => Helpers.Animations.ReduceMotionChanged -= OnReduceMotionChanged;
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (TaskProgress)d;
        if ((bool)e.NewValue) c.Start(); else c.Finish();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (TaskProgress)d;
        if (!c.IsActive || double.IsNaN((double)e.NewValue)) return;
        c.AnimateProgress(Math.Clamp((double)e.NewValue, 0, 99), TimeSpan.FromMilliseconds(300));
    }

    private void OnReduceMotionChanged()
    {
        if (!IsActive || Visibility != Visibility.Visible) return;
        ApplyPulse();
    }

    /// <summary>Starts the busy pulse loop, or holds it steady under Reduce motion.</summary>
    private void ApplyPulse()
    {
        if (Helpers.Animations.ReduceMotion)
        {
            BeginAnimation(PulseProperty, null);
            Pulse = 1;
            return;
        }
        BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.85))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private void Start()
    {
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        Visibility = Visibility.Visible;

        BeginAnimation(DisplayProgressProperty, null);
        DisplayProgress = 0;
        if (double.IsNaN(Value)) AnimateProgress(Ceiling, TimeSpan.FromSeconds(RampSeconds));
        else AnimateProgress(Math.Clamp(Value, 0, 99), TimeSpan.FromMilliseconds(300));

        ApplyPulse();
    }

    private void Finish()
    {
        if (Visibility != Visibility.Visible) return; // never started
        BeginAnimation(PulseProperty, null); Pulse = 1;

        AnimateProgress(100, TimeSpan.FromMilliseconds(260));

        if (Helpers.Animations.ReduceMotion)
        {
            // No fade under Reduce motion: show 100%, hold, then collapse.
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(HoldSeconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!IsActive) Visibility = Visibility.Collapsed;
            };
            timer.Start();
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromSeconds(HoldSeconds) };
        fade.Completed += (_, _) => { if (!IsActive) Visibility = Visibility.Collapsed; };
        BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateProgress(double to, TimeSpan dur)
    {
        if (Helpers.Animations.ReduceMotion)
        {
            // Progress must still be conveyed — just snapped instead of eased.
            BeginAnimation(DisplayProgressProperty, null);
            DisplayProgress = to;
            return;
        }
        BeginAnimation(DisplayProgressProperty, new DoubleAnimation(DisplayProgress, to, dur)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        const double labelW = 50;
        double pillW = Math.Max(0, w - labelW);
        double r = Math.Max(0, (h - 1) / 2);

        dc.DrawRoundedRectangle(_track, _border,
            new Rect(0.7, 0.7, Math.Max(0, pillW - 1.4), Math.Max(0, h - 1.4)), r, r);

        double pct = Math.Clamp(DisplayProgress, 0, 100) / 100.0;

        // tick segments inside the pill
        double padX = h * 0.55, padY = h * 0.26;
        double innerX = padX, innerY = padY, innerW = pillW - padX * 2, innerH = h - padY * 2;
        if (innerW > 4 && innerH > 0)
        {
            const double period = 6.0, tickW = 3.0;
            int count = Math.Max(1, (int)(innerW / period));
            double tickR = tickW / 2;
            int headIndex = (int)Math.Floor(pct * count - 0.5); // last lit tick

            for (int i = 0; i < count; i++)
            {
                double x = innerX + i * period;
                var rect = new Rect(x, innerY, tickW, innerH);

                if (i > headIndex)
                {
                    dc.DrawRoundedRectangle(_unlit, null, rect, tickR, tickR);
                    continue;
                }

                bool head = i == headIndex;
                double t = headIndex > 0 ? (double)i / headIndex : 1; // blue at left -> cyan at head
                var brush = new SolidColorBrush(head ? HeadColor : Lerp(DeepBlue, _accent, t))
                { Opacity = head ? 0.7 + 0.3 * Pulse : 1.0 };
                brush.Freeze();
                dc.DrawRoundedRectangle(brush, null, rect, tickR, tickR);
            }
        }

        // percentage at the end
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText($"{(int)Math.Round(pct * 100)}%", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _num, 14, _label, dpi);
        dc.DrawText(ft, new Point(pillW + 10, (h - ft.Height) / 2));
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
