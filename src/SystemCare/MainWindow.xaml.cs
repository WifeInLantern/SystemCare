using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SystemCare.Controls;
using SystemCare.Services;
using SystemCare.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare;

public partial class MainWindow
{
    private readonly ISettingsService _settings;

    /// <summary>Set by the tray "Exit" menu so the window really closes instead of hiding.</summary>
    public bool ForceExit { get; set; }

    public MainWindow(
        IPageService pageService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        ISettingsService settings)
    {
        _settings = settings;
        InitializeComponent();

        RootNavigation.SetPageService(pageService);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        // WPF-UI's NavigationView hosts pages with unconstrained height, so a page's own
        // ScrollViewer never activates (content is clipped, no scrollbar/wheel). Cap each
        // navigated page's height to the visible content area so it scrolls normally.
        RootNavigation.Navigated += OnNavigated;

        // The nav transition must respect Reduce motion, live: FadeInWithSlide normally,
        // no transition when reduced. (XAML sets the default; this keeps it in sync.)
        ApplyNavigationTransition();
        Helpers.Animations.ReduceMotionChanged += ApplyNavigationTransition;
        Closed += (_, _) => Helpers.Animations.ReduceMotionChanged -= ApplyNavigationTransition;

        StateChanged += OnStateChanged;
        Closing += OnClosing;

        // Ctrl+K command palette: global hotkey + navigate on pick.
        PreviewKeyDown += OnWindowKeyDown;
        Palette.Invoked += type => RootNavigation.Navigate(type);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Palette.Toggle();
            e.Handled = true;
        }
    }

    /// <summary>v6: the "Search" nav item is a visible entry point for the Ctrl+K palette.
    /// It has no TargetPageType, so activation is handled here instead of by navigation.</summary>
    private void OnSearchNavClick(object sender, MouseButtonEventArgs e)
    {
        Palette.Open();
        e.Handled = true;
    }

    private void OnSearchNavKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            Palette.Open();
            e.Handled = true;
        }
    }

    /// <summary>Builds the palette's tool list from the live navigation items (no duplicate registry).</summary>
    private List<NavEntry> BuildNavCatalog()
    {
        var list = new List<NavEntry>();

        void Walk(IEnumerable items)
        {
            string category = "General";
            foreach (var obj in items)
            {
                if (obj is NavigationViewItemHeader header)
                    category = header.Text ?? category;
                else if (obj is NavigationViewItem item && item.TargetPageType is Type pageType && item.Content is string title)
                    list.Add(new NavEntry(title, category, pageType));
            }
        }

        Walk(RootNavigation.MenuItems);
        Walk(RootNavigation.FooterMenuItems);
        return list;
    }

    private void ApplyNavigationTransition() =>
        RootNavigation.Transition = Helpers.Animations.ReduceMotion
            ? Wpf.Ui.Animations.Transition.None
            : Wpf.Ui.Animations.Transition.FadeInWithSlide;

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ConstrainActivePageHeight));

    private void ConstrainActivePageHeight()
    {
        var page = FindDescendant<Page>(RootNavigation);
        if (page is null) return;
        page.SetBinding(MaxHeightProperty, new Binding(nameof(ActualHeight)) { Source = RootNavigation });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is T deeper) return deeper;
        }
        return null;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.Current.MinimizeToTray)
            Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ForceExit && _settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate(typeof(DashboardPage));
        Palette.SetCatalog(BuildNavCatalog());
        PlayEntranceAnimation();
    }

    private void PlayEntranceAnimation()
    {
        if (Helpers.Animations.ReduceMotion) return;

        // A Window can't carry a RenderTransform, so fade the window via Opacity
        // and scale its content grid instead.
        Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(320);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        if (RootGrid.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.985, 1, duration) { EasingFunction = ease });
        }
    }

    /// <summary>Programmatic navigation for the dashboard's quick-link cards.</summary>
    public void NavigateTo(string page)
    {
        Type? target = page switch
        {
            "Cleanup" => typeof(CleanupPage),
            "Startup" => typeof(StartupPage),
            "Privacy" => typeof(PrivacyPage),
            "Disk" => typeof(DiskAnalyzerPage),
            "Duplicates" => typeof(DuplicateFinderPage),
            "SystemInfo" => typeof(SystemInfoPage),
            "Uninstaller" => typeof(SoftwareUninstallerPage),
            "Processes" => typeof(ProcessServicesPage),
            "DiskHealth" => typeof(DiskHealthPage),
            "RescueCenter" => typeof(RescueCenterPage),
            "Security" => typeof(SecurityCheckupPage),
            "Reliability" => typeof(ReliabilityPage),
            "Network" => typeof(NetworkToolsPage),
            "NetMonitor" => typeof(NetworkMonitorPage),
            "SoftwareUpdater" => typeof(SoftwareUpdatePage),
            "CareReport" => typeof(CareReportPage),
            "AutoCare" => typeof(AutoCarePage),
            "WindowsTweaks" => typeof(WindowsTweaksPage),
            "Boost" => typeof(BoostPage),
            "FileShredder" => typeof(FileShredderPage),
            "RegistryCleaner" => typeof(RegistryCleanerPage),
            "EmptyFolders" => typeof(EmptyFolderPage),
            "DeepCleanup" => typeof(DeepCleanupPage),
            "Bloatware" => typeof(BloatwarePage),
            "Settings" => typeof(SettingsPage),
            _ => null,
        };
        if (target is not null)
            RootNavigation.Navigate(target);
    }
}
