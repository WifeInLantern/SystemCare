using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SystemCare.Controls;

/// <summary>
/// Task loading indicator: a row of vertical bars that fill left-to-right as progress rises, with a
/// live percentage and a success checkmark on completion. Replaces the old indeterminate ProgressBar.
/// Drive it with <see cref="IsActive"/> (the task's busy flag). When no real progress is available the
/// percentage is simulated - it eases toward a ceiling (&lt;100%) while busy and only snaps to 100%
/// when the task finishes. Bind <see cref="Value"/> (0-100) when a real percentage exists.
/// </summary>
public class TaskProgress : FrameworkElement
{
    private const double Ceiling = 92;      // simulated progress never passes this while busy
    private const double RampSeconds = 9;   // asymptotic climb toward the ceiling
    private const double HoldSeconds = 1.1; // keep the checkmark up after completion

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(TaskProgress), new PropertyMetadata(false, OnIsActiveChanged));
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TaskProgress), new PropertyMetadata(double.NaN, OnValueChanged));
    /// <summary>Real progress 0-100; leave NaN to simulate.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount), typeof(int), typeof(TaskProgress),
        new FrameworkPropertyMetadata(7, FrameworkPropertyMetadataOptions.AffectsRender));
    public int BarCount { get => (int)GetValue(BarCountProperty); set => SetValue(BarCountProperty, value); }

    private static readonly DependencyProperty DisplayProgressProperty = DependencyProperty.Register(
        nameof(DisplayProgress), typeof(double), typeof(TaskProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private double DisplayProgress { get => (double)GetValue(DisplayProgressProperty); set => SetValue(DisplayProgressProperty, value); }

    private static readonly DependencyProperty PulseProperty = DependencyProperty.Register(
        nameof(Pulse), typeof(double), typeof(TaskProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }

    private static readonly DependencyProperty CompletionProperty = DependencyProperty.Register(
        nameof(Completion), typeof(double), typeof(TaskProgress),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private double Completion { get => (double)GetValue(CompletionProperty); set => SetValue(CompletionProperty, value); }

    private static readonly Color Cyan = Color.FromRgb(0x00, 0xE5, 0xFF);
    private static readonly Color Mint = Color.FromRgb(0x00, 0xFF, 0xA3);
    private readonly Brush _track = new SolidColorBrush(Color.FromRgb(0x16, 0x20, 0x2E));
    private readonly Typeface _num = new(new FontFamily("pack://application:,,,/Assets/Fonts/#Rajdhani"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public TaskProgress()
    {
        Height = 26;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Visibility = Visibility.Collapsed;
        _track.Freeze();
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
        BeginAnimation(CompletionProperty, null); Completion = 0;
        Visibility = Visibility.Visible;

        BeginAnimation(DisplayProgressProperty, null);
        DisplayProgress = 0;
        if (double.IsNaN(Value)) AnimateProgress(Ceiling, TimeSpan.FromSeconds(RampSeconds));
        else AnimateProgress(Math.Clamp(Value, 0, 99), TimeSpan.FromMilliseconds(300));

        BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.05))
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
        BeginAnimation(CompletionProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
        });

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

        int n = Math.Max(1, BarCount);
        double pct = Math.Clamp(DisplayProgress, 0, 100);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Color accent = Lerp(Cyan, Mint, Completion);

        const double labelW = 46;
        double gap = 4;
        double barW = Math.Max(2.0, (Math.Max(0, w - labelW) - gap * (n - 1)) / n);
        double filled = pct / 100.0 * n;

        for (int i = 0; i < n; i++)
        {
            double x = i * (barW + gap);
            double profile = 0.5 + 0.5 * Math.Sin(Math.PI * (i + 0.5) / n); // arch
            double barH = Math.Max(4, h * (0.55 + 0.45 * profile));
            dc.DrawRoundedRectangle(_track, null, new Rect(x, h - barH, barW, barH), barW / 2, barW / 2);

            double fill = Math.Clamp(filled - i, 0, 1);
            if (fill <= 0) continue;

            bool active = (int)filled == i && Completion < 1;
            var brush = new SolidColorBrush(accent) { Opacity = active ? 0.55 + 0.45 * Pulse : 1.0 };
            brush.Freeze();
            double fh = barH * fill;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, h - fh, barW, fh), barW / 2, barW / 2);
        }

        var labelBrush = new SolidColorBrush(Lerp(Cyan, Mint, Completion));
        labelBrush.Freeze();
        var ft = new FormattedText($"{(int)Math.Round(pct)}%", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _num, 13, labelBrush, dpi);
        dc.DrawText(ft, new Point(w - labelW + 4, (h - ft.Height) / 2));

        if (Completion > 0)
        {
            double s = Completion, size = 14 * s, bx = w - labelW - 16, by = h / 2;
            var pen = new Pen(new SolidColorBrush(Mint) { Opacity = s }, 2.2)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(bx - size * 0.45, by), false, false);
                g.LineTo(new Point(bx - size * 0.10, by + size * 0.40), true, false);
                g.LineTo(new Point(bx + size * 0.50, by - size * 0.45), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
