using System.Collections.Generic;
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

    private static bool _reduceMotion;

    /// <summary>
    /// When true (driven by the "Reduce motion &amp; effects" setting), looping/ambient animations are
    /// skipped and entrances settle to their final state immediately. Set at startup and on toggle.
    /// Changing it raises <see cref="ReduceMotionChanged"/> so ambient hosts (the animated backdrop,
    /// neon pulses, the health gauge) can start or freeze in place instead of waiting for their next
    /// trigger — otherwise turning the setting back off would leave them dormant until a reload.
    /// </summary>
    public static bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (_reduceMotion == value) return;
            _reduceMotion = value;
            ReduceMotionChanged?.Invoke();
        }
    }

    /// <summary>Raised on the UI thread whenever <see cref="ReduceMotion"/> changes.</summary>
    public static event Action? ReduceMotionChanged;

    static Animations()
    {
        ReduceMotionChanged += ReapplyNeonPulses;
        ReduceMotionChanged += ReapplyShimmers;
    }

    /// <summary>Neon cyan used for hover/reveal/pulse glows across the cyberpunk theme.</summary>
    private static Color NeonCyan => CyberPalette.Accent;

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
        if (ReduceMotion) { element.Opacity = 1; return; }
        var (_, translate) = GetTransforms(element);
        element.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = Motion.Entrance;
        var delay = TimeSpan.FromMilliseconds(GetRevealDelay(element));

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease, BeginTime = delay });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duration) { EasingFunction = GetEntrancePositionEase(element), BeginTime = delay });
    }

    // ---------- EntranceSpring (opt-in subtle overshoot on FadeInOnLoad/RevealOnLoad's Y-rise) ----------

    /// <summary>
    /// Set alongside FadeInOnLoad/RevealOnLoad on a hero element to give its Y-translate entrance a
    /// small spring overshoot (BackEase) instead of a plain ease-out. Opacity is never affected (a
    /// bouncing opacity reads as flicker, not spring) and this never touches Effect, so it's
    /// Freezable-rule-safe by construction. Intended for a couple of true heroes per page (e.g. the
    /// health gauge), not bulk StaggerChildren cascades — many overshooting siblings at once looks
    /// busy rather than smooth.
    /// </summary>
    public static readonly DependencyProperty EntranceSpringProperty =
        DependencyProperty.RegisterAttached(
            "EntranceSpring", typeof(bool), typeof(Animations), new PropertyMetadata(false));

    public static bool GetEntranceSpring(DependencyObject o) => (bool)o.GetValue(EntranceSpringProperty);
    public static void SetEntranceSpring(DependencyObject o, bool v) => o.SetValue(EntranceSpringProperty, v);

    private static IEasingFunction GetEntrancePositionEase(FrameworkElement element) =>
        GetEntranceSpring(element)
            ? new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseOut };

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
        // v4: was an instant static-Effect pop; now fades in like HoverGlow (same visual strength,
        // 28/0.7, only the pop->fade mechanics changed).
        FadeGlowIn(element, NeonCyan, 28, 0.7);
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
        FadeGlowOut(element);
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
        if (ReduceMotion)
        {
            range.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            range.Value = to;
            return;
        }
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

    // Live hosts of a NeonPulse glow, tracked so a Reduce-motion toggle can restart or freeze their
    // loop in place (weak refs so unloaded elements are collected; pruned lazily on reapply).
    private static readonly List<WeakReference<FrameworkElement>> NeonPulseHosts = new();

    private static void OnNeonPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            bool tracked = false;
            foreach (var w in NeonPulseHosts)
                if (w.TryGetTarget(out var t) && ReferenceEquals(t, element)) { tracked = true; break; }
            if (!tracked) NeonPulseHosts.Add(new WeakReference<FrameworkElement>(element));
            ApplyNeonPulse(element);
        }
        else
        {
            element.Effect = null;
        }
    }

    /// <summary>Applies the cyan glow: a forever-breathing loop, or a static glow under reduced motion.</summary>
    private static void ApplyNeonPulse(FrameworkElement element)
    {
        var glow = new DropShadowEffect { Color = NeonCyan, ShadowDepth = 0, BlurRadius = 24, Opacity = 0.4 };
        element.Effect = glow;
        if (ReduceMotion) return; // keep a static glow, skip the forever-breathing loop
        glow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.3, 0.8, Motion.Loop)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    /// <summary>On a Reduce-motion toggle, restart or freeze every still-living NeonPulse host in place.</summary>
    private static void ReapplyNeonPulses()
    {
        for (int i = NeonPulseHosts.Count - 1; i >= 0; i--)
        {
            if (NeonPulseHosts[i].TryGetTarget(out var element))
            {
                if (GetNeonPulse(element)) ApplyNeonPulse(element);
            }
            else
            {
                NeonPulseHosts.RemoveAt(i);
            }
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
        if (ReduceMotion) { element.Opacity = 1; return; }
        var (_, translate) = GetTransforms(element);
        element.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = Motion.Reveal;
        var delay = TimeSpan.FromMilliseconds(GetRevealDelay(element));

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease, BeginTime = delay });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, duration) { EasingFunction = GetEntrancePositionEase(element), BeginTime = delay });

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
        if (double.IsNaN(from) || ReduceMotion)
        {
            // First value (or reduced motion): set instantly instead of counting up.
            tb.BeginAnimation(CountUpCurrentProperty, null);
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

    // ---------- StaggerChildren (auto-staggered entrance for a panel's children) ----------

    /// <summary>
    /// Set on a Panel to give its children a staggered FadeInOnLoad cascade automatically.
    /// Children that are collapsed, marked <see cref="StaggerExcludeProperty"/>, or already
    /// annotated with FadeInOnLoad/RevealOnLoad (hand-tuned cascades) are skipped.
    /// </summary>
    public static readonly DependencyProperty StaggerChildrenProperty =
        DependencyProperty.RegisterAttached(
            "StaggerChildren", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnStaggerChildrenChanged));

    public static bool GetStaggerChildren(DependencyObject o) => (bool)o.GetValue(StaggerChildrenProperty);
    public static void SetStaggerChildren(DependencyObject o, bool v) => o.SetValue(StaggerChildrenProperty, v);

    /// <summary>Delay increment (ms) between staggered siblings. Total delay is capped at 400ms.</summary>
    public static readonly DependencyProperty StaggerStepProperty =
        DependencyProperty.RegisterAttached(
            "StaggerStep", typeof(double), typeof(Animations), new PropertyMetadata(Motion.StaggerStepMs));

    public static double GetStaggerStep(DependencyObject o) => (double)o.GetValue(StaggerStepProperty);
    public static void SetStaggerStep(DependencyObject o, double v) => o.SetValue(StaggerStepProperty, v);

    /// <summary>Set on a child of a StaggerChildren panel to opt it out of the cascade.</summary>
    public static readonly DependencyProperty StaggerExcludeProperty =
        DependencyProperty.RegisterAttached(
            "StaggerExclude", typeof(bool), typeof(Animations), new PropertyMetadata(false));

    public static bool GetStaggerExclude(DependencyObject o) => (bool)o.GetValue(StaggerExcludeProperty);
    public static void SetStaggerExclude(DependencyObject o, bool v) => o.SetValue(StaggerExcludeProperty, v);

    private static void OnStaggerChildrenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.Panel panel || !(bool)e.NewValue) return;
        if (panel.IsLoaded)
            ApplyStagger(panel);
        else
            panel.Loaded += OnStaggerPanelLoaded;
    }

    private static void OnStaggerPanelLoaded(object sender, RoutedEventArgs e)
    {
        var panel = (System.Windows.Controls.Panel)sender;
        panel.Loaded -= OnStaggerPanelLoaded;
        ApplyStagger(panel);
    }

    private static void ApplyStagger(System.Windows.Controls.Panel panel)
    {
        double step = GetStaggerStep(panel);
        int i = 0;
        foreach (var child in panel.Children)
        {
            if (child is not FrameworkElement element) continue;
            if (element.Visibility == Visibility.Collapsed) continue;
            if (GetStaggerExclude(element)) continue;
            if (GetFadeInOnLoad(element) || GetRevealOnLoad(element)) continue; // hand-tuned cascade wins

            SetRevealDelay(element, Math.Min(i * step, Motion.StaggerCapMs));
            SetFadeInOnLoad(element, true);
            i++;
        }
    }

    // ---------- FadeVisible (animated visibility swap for loading/result states) ----------

    /// <summary>
    /// Animated replacement for a Visibility binding: true fades the element in (with a small
    /// rise), false fades it out and collapses it. The first application (initial binding value)
    /// applies instantly so nothing flickers at load. Nullable so the initial binding always fires.
    /// </summary>
    public static readonly DependencyProperty FadeVisibleProperty =
        DependencyProperty.RegisterAttached(
            "FadeVisible", typeof(bool?), typeof(Animations),
            new PropertyMetadata(null, OnFadeVisibleChanged));

    public static bool? GetFadeVisible(DependencyObject o) => (bool?)o.GetValue(FadeVisibleProperty);
    public static void SetFadeVisible(DependencyObject o, bool? v) => o.SetValue(FadeVisibleProperty, v);

    private static void OnFadeVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not bool visible) return;

        bool firstSet = e.OldValue is null;
        if (firstSet || ReduceMotion)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var (_, translate) = GetTransforms(element);
        if (visible)
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Base) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(4, 0, Motion.Base) { EasingFunction = ease });
        }
        else
        {
            var fadeOut = new DoubleAnimation(0, Motion.Fast) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                // Re-check: a rapid re-toggle back to visible must win over this collapse.
                if (GetFadeVisible(element) == false)
                    element.Visibility = Visibility.Collapsed;
            };
            element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }

    // ---------- PressScale (tactile press-down / spring-back for buttons) ----------

    public static readonly DependencyProperty PressScaleProperty =
        DependencyProperty.RegisterAttached(
            "PressScale", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnPressScaleChanged));

    public static bool GetPressScale(DependencyObject o) => (bool)o.GetValue(PressScaleProperty);
    public static void SetPressScale(DependencyObject o, bool v) => o.SetValue(PressScaleProperty, v);

    private static void OnPressScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            GetTransforms(element); // give it a private transform up front
            element.PreviewMouseLeftButtonDown += OnPressDown;
            element.PreviewMouseLeftButtonUp += OnPressRelease;
            element.MouseLeave += OnPressRelease;
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= OnPressDown;
            element.PreviewMouseLeftButtonUp -= OnPressRelease;
            element.MouseLeave -= OnPressRelease;
        }
    }

    private static void OnPressDown(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var (scale, _) = GetTransforms(element);
        if (ReduceMotion)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 0.96;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, Motion.Fast) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, Motion.Fast) { EasingFunction = ease });
    }

    private static void OnPressRelease(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var (scale, _) = GetTransforms(element);
        if (ReduceMotion)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
            return;
        }
        var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Motion.Base) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Motion.Base) { EasingFunction = ease });
    }

    // ---------- HoverGlow (a glow that fades in/out on hover instead of popping) ----------

    /// <summary>
    /// Animated hover glow. Unlike a Style Effect trigger (which snaps), the glow's opacity fades
    /// in over 200ms and out over 300ms. Mutually exclusive with HoverLift/NeonPulse on the same
    /// element (each behavior owns <see cref="UIElement.Effect"/>).
    /// </summary>
    public static readonly DependencyProperty HoverGlowProperty =
        DependencyProperty.RegisterAttached(
            "HoverGlow", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnHoverGlowChanged));

    public static bool GetHoverGlow(DependencyObject o) => (bool)o.GetValue(HoverGlowProperty);
    public static void SetHoverGlow(DependencyObject o, bool v) => o.SetValue(HoverGlowProperty, v);

    /// <summary>Glow color; leave unset for the theme accent (resolved at hover time).</summary>
    public static readonly DependencyProperty HoverGlowColorProperty =
        DependencyProperty.RegisterAttached(
            "HoverGlowColor", typeof(Color), typeof(Animations), new PropertyMetadata(default(Color)));

    public static Color GetHoverGlowColor(DependencyObject o) => (Color)o.GetValue(HoverGlowColorProperty);
    public static void SetHoverGlowColor(DependencyObject o, Color v) => o.SetValue(HoverGlowColorProperty, v);

    // Tracks the effect instance this behavior currently owns on an element.
    private static readonly DependencyProperty HoverGlowEffectProperty =
        DependencyProperty.RegisterAttached(
            "HoverGlowEffect", typeof(DropShadowEffect), typeof(Animations), new PropertyMetadata(null));

    private static void OnHoverGlowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            element.MouseEnter += OnHoverGlowEnter;
            element.MouseLeave += OnHoverGlowLeave;
        }
        else
        {
            element.MouseEnter -= OnHoverGlowEnter;
            element.MouseLeave -= OnHoverGlowLeave;
            if (element.GetValue(HoverGlowEffectProperty) is DropShadowEffect owned &&
                ReferenceEquals(element.Effect, owned))
            {
                element.Effect = null;
            }
            element.ClearValue(HoverGlowEffectProperty);
        }
    }

    private static void OnHoverGlowEnter(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var color = GetHoverGlowColor(element);
        if (color == default) color = NeonCyan;
        FadeGlowIn(element, color, 18, 0.55);
    }

    private static void OnHoverGlowLeave(object sender, RoutedEventArgs e) => FadeGlowOut((FrameworkElement)sender);

    /// <summary>
    /// Shared fade-in primitive for hover glows (used by <see cref="HoverGlow"/> and
    /// <see cref="HoverLift"/>): fades a per-element <see cref="DropShadowEffect"/> in to
    /// <paramref name="toOpacity"/> over <see cref="Motion.Base"/>, reusing the effect instance if
    /// one is already mid fade-out from a previous hover. Snaps instantly under ReduceMotion.
    /// </summary>
    private static void FadeGlowIn(FrameworkElement element, Color color, double blur, double toOpacity)
    {
        if (element.GetValue(HoverGlowEffectProperty) is not DropShadowEffect glow ||
            !ReferenceEquals(element.Effect, glow))
        {
            glow = new DropShadowEffect { Color = color, ShadowDepth = 0, BlurRadius = blur, Opacity = 0 };
            element.Effect = glow;
            element.SetValue(HoverGlowEffectProperty, glow);
        }
        glow.Color = color;
        glow.BlurRadius = blur;

        if (ReduceMotion)
        {
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            glow.Opacity = toOpacity;
            return;
        }
        glow.BeginAnimation(DropShadowEffect.OpacityProperty,
            new DoubleAnimation(toOpacity, Motion.Base) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Shared fade-out primitive: fades the tracked glow to 0 over <see cref="Motion.Gentle"/>,
    /// then clears <see cref="UIElement.Effect"/> if still owned and the pointer hasn't re-entered.
    /// Snaps instantly under ReduceMotion.</summary>
    private static void FadeGlowOut(FrameworkElement element)
    {
        if (element.GetValue(HoverGlowEffectProperty) is not DropShadowEffect glow ||
            !ReferenceEquals(element.Effect, glow))
        {
            return;
        }

        if (ReduceMotion)
        {
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            element.Effect = null;
            return;
        }
        var fade = new DoubleAnimation(0, Motion.Gentle) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) =>
        {
            // Only clear if we still own the effect and the pointer hasn't re-entered.
            if (ReferenceEquals(element.Effect, glow) && !element.IsMouseOver)
                element.Effect = null;
        };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
    }

    // ---------- Shimmer (opacity breathing for skeleton loading placeholders) ----------

    public static readonly DependencyProperty ShimmerProperty =
        DependencyProperty.RegisterAttached(
            "Shimmer", typeof(bool), typeof(Animations),
            new PropertyMetadata(false, OnShimmerChanged));

    public static bool GetShimmer(DependencyObject o) => (bool)o.GetValue(ShimmerProperty);
    public static void SetShimmer(DependencyObject o, bool v) => o.SetValue(ShimmerProperty, v);

    // Live shimmer hosts, tracked like NeonPulseHosts so a Reduce-motion toggle freezes/restarts them.
    private static readonly List<WeakReference<FrameworkElement>> ShimmerHosts = new();

    private static void OnShimmerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            bool tracked = false;
            foreach (var w in ShimmerHosts)
                if (w.TryGetTarget(out var t) && ReferenceEquals(t, element)) { tracked = true; break; }
            if (!tracked) ShimmerHosts.Add(new WeakReference<FrameworkElement>(element));
            ApplyShimmer(element);
        }
        else
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        }
    }

    private static void ApplyShimmer(FrameworkElement element)
    {
        if (ReduceMotion)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 0.55; // static mid-breath placeholder
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.35, 0.75, Motion.ShimmerLoop)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    /// <summary>On a Reduce-motion toggle, freeze or restart every still-living shimmer host.</summary>
    private static void ReapplyShimmers()
    {
        for (int i = ShimmerHosts.Count - 1; i >= 0; i--)
        {
            if (ShimmerHosts[i].TryGetTarget(out var element))
            {
                if (GetShimmer(element)) ApplyShimmer(element);
            }
            else
            {
                ShimmerHosts.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Motion design tokens: durations for the cyberpunk motion language. Interactive feedback never
/// exceeds Gentle (300ms); hover is in=Base, out=Gentle; only entrances go longer via stagger delay.
/// Documented in docs/DESIGN-SYSTEM.md.
/// </summary>
public static class Motion
{
    public const double FastMs = 120;        // press-down, chip hover, focus glow in
    public const double BaseMs = 200;        // hover-in glow/lift, press release
    public const double GentleMs = 300;      // hover-out, fade-swap, badge changes
    public const double EntranceMs = 280;    // FadeInOnLoad (v4: was 260)
    public const double RevealMs = 340;      // RevealOnLoad (v4: was 320)
    public const double StaggerStepMs = 40;  // delay increment between staggered siblings
    public const double StaggerCapMs = 400;  // total stagger delay ceiling
    public const double LoopMs = 1600;       // NeonPulse breathing
    public const double ShimmerLoopMs = 1100;

    public static TimeSpan Fast => TimeSpan.FromMilliseconds(FastMs);
    public static TimeSpan Base => TimeSpan.FromMilliseconds(BaseMs);
    public static TimeSpan Gentle => TimeSpan.FromMilliseconds(GentleMs);
    public static TimeSpan Entrance => TimeSpan.FromMilliseconds(EntranceMs);
    public static TimeSpan Reveal => TimeSpan.FromMilliseconds(RevealMs);
    public static TimeSpan Loop => TimeSpan.FromMilliseconds(LoopMs);
    public static TimeSpan ShimmerLoop => TimeSpan.FromMilliseconds(ShimmerLoopMs);
}
