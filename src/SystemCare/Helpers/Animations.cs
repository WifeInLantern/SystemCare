using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SystemCare.Helpers;

/// <summary>
/// Code-driven attached behaviors for lightweight animations. These deliberately
/// assign a fresh per-element <see cref="TransformGroup"/> rather than animating a
/// transform set through a Style setter — a setter-applied Freezable is frozen and
/// shared across all controls using the style, which makes WPF throw
/// "Cannot resolve all property references in the property path 'RenderTransform.ScaleX'".
/// </summary>
public static class Animations
{
    private const int ScaleIndex = 0;
    private const int TranslateIndex = 1;

    /// <summary>Neon cyan used for hover/reveal/pulse glows across the cyberpunk theme.</summary>
    private static readonly Color NeonCyan = Color.FromRgb(0x00, 0xE5, 0xFF);

    /// <summary>Ensures the element owns a private [Scale, Translate] transform group, centered.</summary>
    private static (ScaleTransform Scale, TranslateTransform Translate) GetTransforms(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup existing &&
            existing.Children.Count == 2 &&
            existing.Children[ScaleIndex] is ScaleTransform es &&
            existing.Children[TranslateIndex] is TranslateTransform et)
        {
            return (es, et);
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };
        return (scale, translate);
    }

    // ---------- FadeInOnLoad ----------

    public static readonly DependencyProperty FadeInOnLoadProperty =
        DependencyProperty.RegisterAttached(
            "FadeInOnLoad", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnFadeInOnLoadChanged));

    public static bool GetFadeInOnLoad(DependencyObject obj) => (bool)obj.GetValue(FadeInOnLoadProperty);
    public static void SetFadeInOnLoad(DependencyObject obj, bool value) => obj.SetValue(FadeInOnLoadProperty, value);

    private static void OnFadeInOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || !(bool)e.NewValue) return;

        if (element.IsLoaded)
            PlayFadeIn(element);
        else
            element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        PlayFadeIn(element);
    }

    private static void PlayFadeIn(FrameworkElement element)
    {
        var (_, translate) = GetTransforms(element);
        element.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(260);
        var delay = TimeSpan.FromMilliseconds(GetRevealDelay(element));

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease, BeginTime = delay });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duration) { EasingFunction = ease, BeginTime = delay });
    }

    // ---------- RevealDelay (stagger: shared by FadeInOnLoad and RevealOnLoad) ----------

    /// <summary>Milliseconds to delay a FadeInOnLoad/RevealOnLoad entrance, for a staggered cascade.</summary>
    public static readonly DependencyProperty RevealDelayProperty =
        DependencyProperty.RegisterAttached(
            "RevealDelay", typeof(double), typeof(Animations), new PropertyMetadata(0.0));

    public static double GetRevealDelay(DependencyObject o) => (double)o.GetValue(RevealDelayProperty);
    public static void SetRevealDelay(DependencyObject o, double v) => o.SetValue(RevealDelayProperty, v);

    // ---------- HoverLift (scale + glow on mouse-over) ----------

    public static readonly DependencyProperty HoverLiftProperty =
        DependencyProperty.RegisterAttached(
            "HoverLift", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject obj) => (bool)obj.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject obj, bool value) => obj.SetValue(HoverLiftProperty, value);

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            GetTransforms(element); // give it a private transform up front
            element.MouseEnter += OnHoverEnter;
            element.MouseLeave += OnHoverLeave;
        }
        else
        {
            element.MouseEnter -= OnHoverEnter;
            element.MouseLeave -= OnHoverLeave;
        }
    }

    private static void OnHoverEnter(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var (scale, _) = GetTransforms(element);
        element.Effect = new DropShadowEffect
        {
            Color = NeonCyan,
            ShadowDepth = 0,
            BlurRadius = 28,
            Opacity = 0.7,
        };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(150);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.02, d) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.02, d) { EasingFunction = ease });
    }

    private static void OnHoverLeave(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var (scale, _) = GetTransforms(element);
        var d = TimeSpan.FromMilliseconds(200);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, d));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, d));
        element.Effect = null;
    }

    // ---------- SmoothValue (glide a ProgressBar/RangeBase) ----------

    public static readonly DependencyProperty SmoothValueProperty =
        DependencyProperty.RegisterAttached(
            "SmoothValue", typeof(double), typeof(Animations),
            new PropertyMetadata(0.0, OnSmoothValueChanged));

    public static double GetSmoothValue(DependencyObject o) => (double)o.GetValue(SmoothValueProperty);
    public static void SetSmoothValue(DependencyObject o, double v) => o.SetValue(SmoothValueProperty, v);

    private static void OnSmoothValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.Primitives.RangeBase range) return;
        double to = (double)e.NewValue;
        range.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(range.Value, to, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    // ---------- NeonPulse (a forever-breathing cyan glow for hero/accent elements) ----------

    public static readonly DependencyProperty NeonPulseProperty =
        DependencyProperty.RegisterAttached(
            "NeonPulse", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnNeonPulseChanged));

    public static bool GetNeonPulse(DependencyObject o) => (bool)o.GetValue(NeonPulseProperty);
    public static void SetNeonPulse(DependencyObject o, bool v) => o.SetValue(NeonPulseProperty, v);

    private static void OnNeonPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            var glow = new DropShadowEffect { Color = NeonCyan, ShadowDepth = 0, BlurRadius = 24, Opacity = 0.4 };
            element.Effect = glow;
            glow.BeginAnimation(DropShadowEffect.OpacityProperty,
                new DoubleAnimation(0.3, 0.8, TimeSpan.FromSeconds(1.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
        }
        else
        {
            element.Effect = null;
        }
    }

    // ---------- RevealOnLoad (fade + rise + a one-shot "power-on" cyan flash) ----------

    public static readonly DependencyProperty RevealOnLoadProperty =
        DependencyProperty.RegisterAttached(
            "RevealOnLoad", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnRevealOnLoadChanged));

    public static bool GetRevealOnLoad(DependencyObject obj) => (bool)obj.GetValue(RevealOnLoadProperty);
    public static void SetRevealOnLoad(DependencyObject obj, bool value) => obj.SetValue(RevealOnLoadProperty, value);

    private static void OnRevealOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || !(bool)e.NewValue) return;
        if (element.IsLoaded)
            PlayReveal(element);
        else
            element.Loaded += OnRevealLoaded;
    }

    private static void OnRevealLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnRevealLoaded;
        PlayReveal(element);
    }

    private static void PlayReveal(FrameworkElement element)
    {
        var (_, translate) = GetTransforms(element);
        element.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(320);
        var delay = TimeSpan.FromMilliseconds(GetRevealDelay(element));

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease, BeginTime = delay });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, duration) { EasingFunction = ease, BeginTime = delay });

        // One-shot cyan "power-on" flash that fades out, then clears the effect.
        var glow = new DropShadowEffect { Color = NeonCyan, ShadowDepth = 0, BlurRadius = 26, Opacity = 0 };
        element.Effect = glow;
        var flash = new DoubleAnimation(0.65, 0, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            BeginTime = delay,
        };
        flash.Completed += (_, _) => { if (ReferenceEquals(element.Effect, glow)) element.Effect = null; };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, flash);
    }

    // ---------- CountUpText (smoothly animate a TextBlock's numeric value) ----------

    /// <summary>Target value; the TextBlock counts toward it (~600ms cubic-out) instead of snapping.</summary>
    public static readonly DependencyProperty CountUpTextProperty =
        DependencyProperty.RegisterAttached(
            "CountUpText", typeof(double), typeof(Animations),
            new PropertyMetadata(double.NaN, OnCountUpTextChanged));

    public static double GetCountUpText(DependencyObject o) => (double)o.GetValue(CountUpTextProperty);
    public static void SetCountUpText(DependencyObject o, double v) => o.SetValue(CountUpTextProperty, v);

    /// <summary>Numeric format applied to the animated value (e.g. <c>0'%'</c>). Ignored when CountUpBytes is set.</summary>
    public static readonly DependencyProperty CountUpFormatProperty =
        DependencyProperty.RegisterAttached(
            "CountUpFormat", typeof(string), typeof(Animations), new PropertyMetadata("0"));

    public static string GetCountUpFormat(DependencyObject o) => (string)o.GetValue(CountUpFormatProperty);
    public static void SetCountUpFormat(DependencyObject o, string v) => o.SetValue(CountUpFormatProperty, v);

    /// <summary>When true, the animated value is byte-formatted (e.g. "12.3 GB") via <see cref="ByteFormatter"/>.</summary>
    public static readonly DependencyProperty CountUpBytesProperty =
        DependencyProperty.RegisterAttached(
            "CountUpBytes", typeof(bool), typeof(Animations), new PropertyMetadata(false));

    public static bool GetCountUpBytes(DependencyObject o) => (bool)o.GetValue(CountUpBytesProperty);
    public static void SetCountUpBytes(DependencyObject o, bool v) => o.SetValue(CountUpBytesProperty, v);

    /// <summary>Text appended after the formatted value (e.g. " / 32.0 GB" for memory).</summary>
    public static readonly DependencyProperty CountUpSuffixProperty =
        DependencyProperty.RegisterAttached(
            "CountUpSuffix", typeof(string), typeof(Animations), new PropertyMetadata(""));

    public static string GetCountUpSuffix(DependencyObject o) => (string)o.GetValue(CountUpSuffixProperty);
    public static void SetCountUpSuffix(DependencyObject o, string v) => o.SetValue(CountUpSuffixProperty, v);

    // Internal animated "current" value; writing it formats the TextBlock.
    private static readonly DependencyProperty CountUpCurrentProperty =
        DependencyProperty.RegisterAttached(
            "CountUpCurrent", typeof(double), typeof(Animations),
            new PropertyMetadata(double.NaN, OnCountUpCurrentChanged));

    private static void OnCountUpTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock tb) return;
        double to = (double)e.NewValue;
        if (double.IsNaN(to)) return;

        double from = (double)tb.GetValue(CountUpCurrentProperty);
        if (double.IsNaN(from))
        {
            // First value: set instantly so the screen doesn't count up from zero on load.
            tb.SetValue(CountUpCurrentProperty, to);
            return;
        }
        if (Math.Abs(from - to) < 0.001) return;

        tb.BeginAnimation(CountUpCurrentProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private static void OnCountUpCurrentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock tb) return;
        double v = (double)e.NewValue;
        if (double.IsNaN(v)) return;

        string body = GetCountUpBytes(tb)
            ? ByteFormatter.Format((long)v)
            : v.ToString(GetCountUpFormat(tb), System.Globalization.CultureInfo.InvariantCulture);
        tb.Text = body + GetCountUpSuffix(tb);
    }
}
