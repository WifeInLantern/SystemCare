using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SystemCare.Controls;
using SystemCare.Helpers;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

// Headless smoke test for Design System v2. Loads the real SystemCare dictionaries, resolves every new
// token/style, and instantiates + lays out the controls that use the app-wide implicit styles and the
// reduced-motion code paths. No window is shown and no admin rights are needed; any exception = FAIL.
internal static class Program
{
    private static readonly List<string> Failures = new();
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        var app = new Application();

        Try("merge design-system dictionaries (like App.xaml)", () =>
        {
            app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
            app.Resources.MergedDictionaries.Add(new ControlsDictionary());
            app.Resources.MergedDictionaries.Add(Load("Styles/Theme.xaml"));
            app.Resources.MergedDictionaries.Add(Load("Styles/Cyberpunk.xaml"));
            app.Resources.MergedDictionaries.Add(Load("Styles/Components.xaml"));
        });

        foreach (var key in new[]
        {
            "SpaceXs","SpaceSm","SpaceMd","SpaceLg","SpaceXl","Space2Xl","PagePadding","PadCard","PadChip",
            "RadiusMd","RadiusPill","PanelInsetBrush","ChipFillBrush","ConsoleBackgroundBrush",
            "ConsoleForegroundBrush","HairlineBrush","OverlayScrimBrush","FocusGlowBrush","GlowSm","GlowMd","GlowLg",
            // Design System v3 (glass + status + motion overhaul)
            "Surface0Brush","Surface1Brush","Surface2Brush","Surface3Brush","GlassRimBrush","GlassSheenBrush",
            "SuccessSubtleBrush","WarningSubtleBrush","DangerSubtleBrush","InfoSubtleBrush","VioletSubtleBrush",
            "AccentSubtleBrush","SuccessSubtleStroke","WarningSubtleStroke","DangerSubtleStroke","InfoSubtleStroke",
            "VioletSubtleStroke","DangerSoftBrush","SuccessGradientBrush","GlowMagentaMd","GlowSuccessSm",
            // Design System v4 (glow/contrast intensification)
            "GlowVioletMd","GlowXl",
            // Design System v6 ("Night City, Refined" — docs/UI-REDESIGN-V5.md)
            "TextQuaternaryBrush","Space3Xl","ContentMaxWidth",
        })
            Try($"token '{key}' resolves", () => { if (app.Resources[key] is null) throw new Exception("not found"); });

        foreach (var key in new[]
        {
            "TextH2","TextBody","TextCaption","CyberSectionHeader","CyberPageTitle","CyberDisplayText",
            "CyberPrimaryButton","CyberGhostButton","CyberDangerButton","CyberCard","CyberInteractiveCard",
            "CyberChip","CyberListRow","CyberConsole",
            // Design System v3
            "TextH3","CyberGlassPanel","CyberGlassPanelRaised","CyberChipSuccess","CyberChipWarning",
            "CyberChipDanger","CyberChipInfo","ChipTextSuccess","ChipTextWarning","ChipTextDanger","ChipTextInfo",
            "SkeletonBlock","SkeletonCard","EmptyStateTitle","EmptyStateHint",
            // Design System v5 (accessibility + compliance pass)
            "TextBodyStrong","TextMetricValue","CyberChipNeutral","ChipTextNeutral","CyberFocusVisual",
            // Design System v6 ("Night City, Refined" — docs/UI-REDESIGN-V5.md)
            "TextMetricHero",
        })
            Try($"style '{key}' resolves", () => { if (app.Resources[key] is not Style) throw new Exception("missing or not a Style"); });

        // Build + lay out the real controls under both motion modes (exercises implicit styles, BasedOn
        // resolution, template application, and the ReduceMotion branches).
        foreach (var reduce in new[] { false, true })
        {
            Animations.ReduceMotion = reduce;
            Try($"instantiate + lay out controls (ReduceMotion={reduce})", () => BuildAndLayout(app));
        }

        // Dialog UserControls built in code (not via navigation) never fail-fast until shown, so load their
        // BAML here to catch XAML-load bugs (e.g. an invalid enum value on a control property).
        Try("instantiate + lay out LeftoverReviewView", () =>
        {
            var view = new SystemCare.Views.Dialogs.LeftoverReviewView();
            view.Measure(new Size(560, 420));
            view.Arrange(new Rect(0, 0, 560, 420));
            view.UpdateLayout();
        });

        // Design-system lint (2.19): the text-opacity anti-pattern silently breaks WCAG AA
        // (e.g. TextSecondary x 0.7 on Surface0 = 4.28:1). The app-wide cleanup is done; this
        // rule keeps it done. Inline FontSize on text is reported as a count but doesn't fail
        // (the ramp migration is tracked separately).
        Try("lint: no Opacity on text elements in Views/*.xaml", () =>
        {
            string? viewsDir = FindViewsDir();
            if (viewsDir is null) return; // packaged/out-of-repo run: nothing to lint

            var offenders = new List<string>();
            int fontSizeCount = 0;
            foreach (var file in Directory.EnumerateFiles(viewsDir, "*.xaml"))
            {
                int lineNo = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNo++;
                    bool isText = line.Contains("TextBlock") || line.TrimStart().StartsWith("<Run");
                    if (!isText) continue;
                    if (line.Contains("Opacity=\"0."))
                        offenders.Add($"{Path.GetFileName(file)}:{lineNo}");
                    if (line.Contains("FontSize=\""))
                        fontSizeCount++;
                }
            }
            Console.WriteLine($"    (info: {fontSizeCount} inline FontSize on text remain — ramp migration backlog)");
            if (offenders.Count > 0)
                throw new Exception($"text Opacity found (breaks AA contrast): {string.Join(", ", offenders.Take(8))}" +
                                    (offenders.Count > 8 ? $" (+{offenders.Count - 8} more)" : ""));
        });

        Console.WriteLine();
        Console.WriteLine($"Smoke test: {_checks - Failures.Count}/{_checks} checks passed.");
        if (Failures.Count > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (var f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }
        Console.WriteLine("ALL PASSED");
        return 0;
    }

    private static ResourceDictionary Load(string rel) =>
        new() { Source = new Uri($"pack://application:,,,/SystemCare;component/{rel}") };

    /// <summary>Walks up from the test binary to the repo's src/SystemCare/Views (null when not in-repo).</summary>
    private static string? FindViewsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "SystemCare", "Views");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void BuildAndLayout(Application app)
    {
        var panel = new StackPanel();

        foreach (var k in new[] { "TextH2", "TextBody", "TextCaption", "CyberSectionHeader", "CyberPageTitle", "CyberDisplayText" })
            panel.Children.Add(new TextBlock { Text = k, Style = (Style)app.Resources[k] });

        foreach (var k in new[] { "CyberPrimaryButton", "CyberGhostButton", "CyberDangerButton" })
            panel.Children.Add(new Wpf.Ui.Controls.Button { Content = k, Style = (Style)app.Resources[k] });

        // Implicit styles (no explicit Style set) — must resolve their BasedOn at runtime.
        panel.Children.Add(new Wpf.Ui.Controls.TextBox { Text = "focus glow" });
        panel.Children.Add(new ComboBox());

        foreach (var k in new[] { "CyberCard", "CyberInteractiveCard" })
            panel.Children.Add(new Wpf.Ui.Controls.Card { Style = (Style)app.Resources[k] });

        panel.Children.Add(new Border { Style = (Style)app.Resources["CyberChip"], Child = new TextBlock { Text = "chip" } });
        panel.Children.Add(new Border { Style = (Style)app.Resources["CyberListRow"] });
        panel.Children.Add(new TextBox { Style = (Style)app.Resources["CyberConsole"], Text = "log line" });

        // Custom controls — HealthGauge.Score set exercises the ReduceMotion branch immediately.
        panel.Children.Add(new HealthGauge { Width = 120, Height = 120, Score = 82 });
        panel.Children.Add(new CyberBackground { Width = 200, Height = 120 });
        panel.Children.Add(new SparklineChart { Width = 200, Height = 60, Values = [10, 40, 25, 80], Max = 100 });
        panel.Children.Add(new BarChart { Width = 200, Height = 60, Values = [0, 3, 7, 2, 9], Max = 9 });

        // TaskProgress active exercises its pulse loop / ReduceMotion snap paths.
        panel.Children.Add(new TaskProgress { Width = 220, IsActive = true });

        // v3 glass/skeleton/chip styles applied to real elements.
        panel.Children.Add(new Border { Style = (Style)app.Resources["CyberGlassPanel"] });
        panel.Children.Add(new Border { Style = (Style)app.Resources["CyberGlassPanelRaised"] });
        panel.Children.Add(new Border { Style = (Style)app.Resources["SkeletonBlock"], Width = 160 });
        panel.Children.Add(new Border { Style = (Style)app.Resources["SkeletonCard"], Width = 160 });
        panel.Children.Add(new Border
        {
            Style = (Style)app.Resources["CyberChipSuccess"],
            Child = new TextBlock { Text = "OK", Style = (Style)app.Resources["ChipTextSuccess"] },
        });

        // Attached behaviors that fire synchronously on set (NeonPulse/SmoothValue/CountUpText).
        var pulse = new Border { Width = 40, Height = 40 };
        Animations.SetNeonPulse(pulse, true);
        Animations.SetHoverLift(pulse, true);
        panel.Children.Add(pulse);

        var bar = new ProgressBar { Maximum = 100, Width = 120, Height = 8 };
        Animations.SetSmoothValue(bar, 60);
        panel.Children.Add(bar);

        var count = new TextBlock();
        Animations.SetCountUpText(count, 42);
        panel.Children.Add(count);

        // v3 behaviors: shimmer loop, animated visibility swap, press feedback, hover glow.
        var shimmer = new Border { Width = 40, Height = 12 };
        Animations.SetShimmer(shimmer, true);
        panel.Children.Add(shimmer);

        var swap = new Border { Width = 40, Height = 12 };
        Animations.SetFadeVisible(swap, true);
        Animations.SetFadeVisible(swap, false);
        panel.Children.Add(swap);

        var press = new Border { Width = 40, Height = 20 };
        Animations.SetPressScale(press, true);
        Animations.SetHoverGlow(press, true);
        panel.Children.Add(press);

        // Auto-staggered entrance panel (children get FadeInOnLoad + incremental delays on Loaded).
        var stagger = new StackPanel();
        stagger.Children.Add(new TextBlock { Text = "a" });
        stagger.Children.Add(new TextBlock { Text = "b" });
        Animations.SetStaggerChildren(stagger, true);
        panel.Children.Add(stagger);

        // v4: EntranceSpring's subtle overshoot, paired with RevealOnLoad as it would be on a hero element.
        var spring = new Border { Width = 40, Height = 40 };
        Animations.SetRevealOnLoad(spring, true);
        Animations.SetEntranceSpring(spring, true);
        panel.Children.Add(spring);

        // Root in a Page so the implicit Page text style is exercised, then force layout without a window.
        var page = new Page { Content = panel };
        page.Measure(new Size(1000, 800));
        page.Arrange(new Rect(0, 0, 1000, 800));
        page.UpdateLayout();
        foreach (var child in panel.Children)
            if (child is FrameworkElement fe) fe.ApplyTemplate();
    }

    private static void Try(string name, Action action)
    {
        _checks++;
        try { action(); Console.WriteLine($"[ok]   {name}"); }
        catch (Exception ex)
        {
            Failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
