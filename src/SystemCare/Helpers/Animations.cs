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
            Color = Color.FromRgb(0x21, 0x96, 0xF3),
            ShadowDepth = 0,
            BlurRadius = 22,
            Opacity = 0.55,
        };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(150);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.015, d) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.015, d) { EasingFunction = ease });
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
}
