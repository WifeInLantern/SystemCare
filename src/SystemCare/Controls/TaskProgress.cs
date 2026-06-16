using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SystemCare.Controls;

/// <summary>
/// Task loading indicator: a horizontal green progress bar that fills left-to-right with a darker
/// leading block, on a dark rounded track. Replaces the old indeterminate ProgressBar.
/// Drive it with <see cref="IsActive"/> (the task's busy flag). When no real progress is available the
/// fill is simulated - it eases toward a ceiling (&lt;100%) while busy and only reaches 100% when the
/// task finishes. Bind <see cref="Value"/> (0-100) when a real percentage exists.
/// </summary>
public class TaskProgress : FrameworkElement
{
    private const double Ceiling = 92;      // simulated progress never passes this while busy
    private const double RampSeconds = 9;   // asymptotic climb toward the ceiling
    private const double HoldSeconds = 0.7; // keep the full bar up after completion

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

    // Lime-green fill (vertical gradient) + a darker leading block, on a near-black track.
    private static readonly Color LeadColor = Color.FromRgb(0x3E, 0x6B, 0x12);
    private readonly Brush _track = new SolidColorBrush(Color.FromRgb(0x06, 0x09, 0x0E));
    private readonly Pen _border = new(new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x55)), 1);
    private readonly LinearGradientBrush _fill = new(
        Color.FromRgb(0xB0, 0xEB, 0x3B), Color.FromRgb(0x86, 0xC8, 0x1A), new Point(0, 0), new Point(0, 1));

    public TaskProgress()
    {
        Height = 18;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Visibility = Visibility.Collapsed;
        _track.Freeze();
        _border.Freeze();
        _fill.Freeze();
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

    private void Start()
    {
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        Visibility = Visibility.Visible;

        BeginAnimation(DisplayProgressProperty, null);
        DisplayProgress = 0;
        if (double.IsNaN(Value)) AnimateProgress(Ceiling, TimeSpan.FromSeconds(RampSeconds));
        else AnimateProgress(Math.Clamp(Value, 0, 99), TimeSpan.FromMilliseconds(300));

        BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private void Finish()
    {
        if (Visibility != Visibility.Visible) return; // never started
        BeginAnimation(PulseProperty, null); Pulse = 0;

        AnimateProgress(100, TimeSpan.FromMilliseconds(260));

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromSeconds(HoldSeconds) };
        fade.Completed += (_, _) => { if (!IsActive) Visibility = Visibility.Collapsed; };
        BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateProgress(double to, TimeSpan dur) =>
        BeginAnimation(DisplayProgressProperty, new DoubleAnimation(DisplayProgress, to, dur)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double r = Math.Min(6, h / 2);
        dc.DrawRoundedRectangle(_track, _border,
            new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)), r, r);

        double pct = Math.Clamp(DisplayProgress, 0, 100) / 100.0;
        double fillW = w * pct;
        if (fillW <= 0.5) return;

        var clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        clip.Freeze();
        dc.PushClip(clip);

        dc.DrawRectangle(_fill, null, new Rect(0, 0, fillW, h));

        // darker leading block at the fill head (hidden once the bar is essentially full)
        if (pct < 0.995)
        {
            double blockW = Math.Min(fillW, 16);
            var lead = new SolidColorBrush(LeadColor) { Opacity = 0.85 + 0.15 * Pulse };
            lead.Freeze();
            dc.DrawRectangle(lead, null, new Rect(fillW - blockW, 0, blockW, h));
        }

        dc.Pop();
    }
}
