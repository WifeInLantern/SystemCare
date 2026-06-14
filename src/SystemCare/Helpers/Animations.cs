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

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

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

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, duration) { EasingFunction = ease });

        // One-shot cyan "power-on" flash that fades out, then clears the effect.
        var glow = new DropShadowEffect { Color = NeonCyan, ShadowDepth = 0, BlurRadius = 26, Opacity = 0 };
        element.Effect = glow;
        var flash = new DoubleAnimation(0.65, 0, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        flash.Completed += (_, _) => { if (ReferenceEquals(element.Effect, glow)) element.Effect = null; };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, flash);
    }
}
