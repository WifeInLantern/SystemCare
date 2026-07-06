using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SystemCare.Services;
using SystemCare.Services.GameBooster;
using SystemCare.ViewModels;
using SystemCare.Views;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Extensions;

namespace SystemCare;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "SystemCare.SingleInstance";
    private const string ActivateEventName = "SystemCare.Activate";

    private static readonly IServiceProvider _services = ConfigureServices();

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;

    public static IServiceProvider Services => _services;

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<IPageService, PageService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILogService, LogService>();

        // Domain services
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IJunkScanService, JunkScanService>();
        services.AddSingleton<IMemoryOptimizerService, MemoryOptimizerService>();
        services.AddSingleton<IHealthScoreService, HealthScoreService>();
        services.AddSingleton<IStartupManagerService, StartupManagerService>();
        services.AddSingleton<IBootPerformanceService, BootPerformanceService>();
        services.AddSingleton<IPrivacyCleanerService, PrivacyCleanerService>();
        services.AddSingleton<IDiskAnalyzerService, DiskAnalyzerService>();
        services.AddSingleton<IDuplicateFinderService, DuplicateFinderService>();
        services.AddSingleton<IFileOperationService, FileOperationService>();
        services.AddSingleton<IHardwareInfoService, HardwareInfoService>();
        services.AddSingleton<ITemperatureService, TemperatureService>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<IInstalledAppsService, InstalledAppsService>();
        services.AddSingleton<ILeftoverScanService, LeftoverScanService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IRecycleBinService, RecycleBinService>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<IScheduledMaintenanceService, ScheduledMaintenanceService>();
        services.AddSingleton<IStartupLauncherService, StartupLauncherService>();
        services.AddSingleton<ILiveMetricsService, LiveMetricsService>();
        services.AddSingleton<IResourceAlertService, ResourceAlertService>();
        services.AddSingleton<IMiniMonitorService, MiniMonitorService>();
        services.AddSingleton<ISensorMonitorService, SensorMonitorService>();
        services.AddSingleton<IReliabilityService, ReliabilityService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IDiskMaintenanceService, DiskMaintenanceService>();
        services.AddSingleton<IDiskHealthScoreService, DiskHealthScoreService>();
        services.AddSingleton<ISystemRepairService, SystemRepairService>();
        services.AddSingleton<IRestorePointService, RestorePointService>();
        services.AddSingleton<IBackupConfirmationService, BackupConfirmationService>();
        services.AddSingleton<IRegistryCleanerService, RegistryCleanerService>();
        services.AddSingleton<IBenchmarkService, BenchmarkService>();
        services.AddSingleton<IBenchmarkHistoryService, BenchmarkHistoryService>();
        services.AddSingleton<IHealthTrendService, HealthTrendService>();
        services.AddSingleton<ICareReportExporter, CareReportExporter>();
        services.AddSingleton<IAutoCareService, AutoCareService>();
        services.AddSingleton<IEmptyFolderService, EmptyFolderService>();
        services.AddSingleton<IDeepCleanupService, DeepCleanupService>();
        services.AddSingleton<IAppPackageService, AppPackageService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ISecurityCheckService, SecurityCheckService>();
        services.AddSingleton<IDefenderService, DefenderService>();
        services.AddSingleton<IBatteryHealthService, BatteryHealthService>();
        services.AddSingleton<IDnsService, DnsService>();
        services.AddSingleton<IRansomwareShieldService, RansomwareShieldService>();
        services.AddSingleton<IHostsBlockerService, HostsBlockerService>();
        services.AddSingleton<IBreachCheckService, BreachCheckService>();
        services.AddSingleton<ILargeFileScanService, LargeFileScanService>();
        services.AddSingleton<IBrowserCleanupService, BrowserCleanupService>();
        services.AddSingleton<IScheduledTaskManagerService, ScheduledTaskManagerService>();
        services.AddSingleton<IContextMenuManagerService, ContextMenuManagerService>();
        services.AddSingleton<ISpeedTestService, SpeedTestService>();
        services.AddSingleton<INetworkToolsService, NetworkToolsService>();
        services.AddSingleton<INetworkUsageService, NetworkUsageService>();
        services.AddSingleton<IFirewallService, FirewallService>();
        services.AddSingleton<IConfirmDialogService, ConfirmDialogService>();
        services.AddSingleton<IDebloatService, DebloatService>();
        services.AddSingleton<IPowerPlanService, PowerPlanService>();
        services.AddSingleton<ITweaksService, TweaksService>();
        services.AddSingleton<IBoostService, BoostService>();
        // Game Booster: reversible-optimization engine + journaled optimizations.
        services.AddSingleton<IRollbackJournal, RollbackJournal>();
        services.AddSingleton<IReversibleOptimization, PowerPlanOptimization>();
        services.AddSingleton<IReversibleOptimization, AppSuspendOptimization>();
        services.AddSingleton<IReversibleOptimization, MemoryOptimization>();
        services.AddSingleton<IReversibleOptimization, NotificationOptimization>();
        services.AddSingleton<IOptimizationEngine, OptimizationEngine>();
        services.AddSingleton<IGameBoosterService, GameBoosterService>();
        services.AddSingleton<IFileShredderService, FileShredderService>();
        services.AddSingleton<IDriverUpdateService, DriverUpdateService>();
        services.AddSingleton<IWingetRunner, WingetRunner>();
        services.AddSingleton<ISoftwareUpdateService, SoftwareUpdateService>();
        services.AddSingleton<ISoftwareHubService, SoftwareHubService>();
        services.AddSingleton<IWindowsUpdateService, WindowsUpdateService>();

        // Window
        services.AddSingleton<MainWindow>();

        // ViewModels — singletons so page state (scan results, timers) survives navigation
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<AutoCareViewModel>();
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<PrivacyViewModel>();
        services.AddSingleton<DiskAnalyzerViewModel>();
        services.AddSingleton<DuplicateFinderViewModel>();
        services.AddSingleton<SystemInfoViewModel>();
        services.AddSingleton<SoftwareUninstallerViewModel>();
        services.AddSingleton<ProcessServicesViewModel>();
        services.AddSingleton<DiskHealthViewModel>();
        services.AddSingleton<RescueCenterViewModel>();
        services.AddSingleton<RegistryCleanerViewModel>();
        services.AddSingleton<EmptyFolderViewModel>();
        services.AddSingleton<DeepCleanupViewModel>();
        services.AddSingleton<BloatwareViewModel>();
        services.AddSingleton<DebloatViewModel>();
        services.AddSingleton<SecurityCheckupViewModel>();
        services.AddSingleton<DefenderViewModel>();
        services.AddSingleton<BatteryHealthViewModel>();
        services.AddSingleton<DnsViewModel>();
        services.AddSingleton<RansomwareShieldViewModel>();
        services.AddSingleton<HostsBlockerViewModel>();
        services.AddSingleton<BreachCheckViewModel>();
        services.AddSingleton<LargeFilesViewModel>();
        services.AddSingleton<BrowserCleanupViewModel>();
        services.AddSingleton<ScheduledTasksViewModel>();
        services.AddSingleton<ContextMenuViewModel>();
        services.AddSingleton<BootAnalyzerViewModel>();
        services.AddSingleton<SpeedTestViewModel>();
        services.AddSingleton<NetworkToolsViewModel>();
        services.AddSingleton<NetworkMonitorViewModel>();
        services.AddSingleton<NetworkSecurityAuditViewModel>();
        services.AddSingleton<WindowsTweaksViewModel>();
        services.AddSingleton<BoostViewModel>();
        services.AddSingleton<GameBoosterViewModel>();
        services.AddSingleton<FileShredderViewModel>();
        services.AddSingleton<DriverUpdateViewModel>();
        services.AddSingleton<SoftwareUpdateViewModel>();
        services.AddSingleton<SoftwareHubViewModel>();
        services.AddSingleton<WindowsUpdateViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<CareReportViewModel>();
        services.AddSingleton<BenchmarkViewModel>();
        services.AddSingleton<SensorsViewModel>();
        services.AddSingleton<ReliabilityViewModel>();
        services.AddSingleton<RepairToolkitViewModel>();

        // Pages
        services.AddTransient<DashboardPage>();
        services.AddTransient<AutoCarePage>();
        services.AddTransient<CleanupPage>();
        services.AddTransient<StartupPage>();
        services.AddTransient<PrivacyPage>();
        services.AddTransient<DiskAnalyzerPage>();
        services.AddTransient<DuplicateFinderPage>();
        services.AddTransient<SystemInfoPage>();
        services.AddTransient<SoftwareUninstallerPage>();
        services.AddTransient<ProcessServicesPage>();
        services.AddTransient<DiskHealthPage>();
        services.AddTransient<RescueCenterPage>();
        services.AddTransient<RegistryCleanerPage>();
        services.AddTransient<EmptyFolderPage>();
        services.AddTransient<DeepCleanupPage>();
        services.AddTransient<BloatwarePage>();
        services.AddTransient<DebloatPage>();
        services.AddTransient<SecurityCheckupPage>();
        services.AddTransient<DefenderPage>();
        services.AddTransient<BatteryHealthPage>();
        services.AddTransient<DnsPage>();
        services.AddTransient<RansomwareShieldPage>();
        services.AddTransient<HostsBlockerPage>();
        services.AddTransient<BreachCheckPage>();
        services.AddTransient<LargeFilesPage>();
        services.AddTransient<BrowserCleanupPage>();
        services.AddTransient<ScheduledTasksPage>();
        services.AddTransient<ContextMenuPage>();
        services.AddTransient<BootAnalyzerPage>();
        services.AddTransient<SpeedTestPage>();
        services.AddTransient<NetworkToolsPage>();
        services.AddTransient<NetworkMonitorPage>();
        services.AddTransient<NetworkSecurityAuditPage>();
        services.AddTransient<WindowsTweaksPage>();
        services.AddTransient<BoostPage>();
        services.AddTransient<GameBoosterPage>();
        services.AddTransient<FileShredderPage>();
        services.AddTransient<DriverUpdatePage>();
        services.AddTransient<SoftwareUpdatePage>();
        services.AddTransient<SoftwareHubPage>();
        services.AddTransient<WindowsUpdatePage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<CareReportPage>();
        services.AddTransient<BenchmarkPage>();
        services.AddTransient<SensorsPage>();
        services.AddTransient<ReliabilityPage>();
        services.AddTransient<RepairToolkitPage>();

        return services.BuildServiceProvider();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch faults off the UI thread too (the XAML wires only DispatcherUnhandledException). Without
        // these, an exception on a background thread or an unobserved Task can tear the process down with
        // nothing logged. Subscribed before the headless/interactive split so both run modes are covered.
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Headless scheduled-maintenance run: no window, balloon, then exit.
        if (e.Args.Contains("--run-maintenance"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var log = _services.GetRequiredService<ILogService>();
            log.Info("Maintenance", "Headless scheduled maintenance started (--run-maintenance).");
            var tray = _services.GetRequiredService<ITrayIconService>();
            tray.Initialize();
            try
            {
                var result = await _services.GetRequiredService<IScheduledMaintenanceService>().RunMaintenanceNowAsync();
                tray.ShowBalloon("SystemCare maintenance complete", result.Summary);
                await Task.Delay(TimeSpan.FromSeconds(6));
            }
            catch (Exception ex) { log.Error("Maintenance", "Headless maintenance failed", ex); }
            finally
            {
                tray.Dispose();
                Shutdown();
            }
            return;
        }

        // Single-instance: if another interactive copy is already running, signal it to come
        // to the front and exit. (Headless --run-maintenance above is exempt and already returned.)
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch (Exception) { }
            Shutdown();
            return;
        }
        StartActivationListener();

        // Cyberpunk theme is dark-only. Apply the dark base, then force the neon
        // cyan accent so every accent-driven Fluent control (buttons, toggles,
        // selection, progress) picks it up.
        var settings = _services.GetRequiredService<ISettingsService>();
        SystemCare.Helpers.Animations.ReduceMotion = settings.Current.ReduceMotion;
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        ApplicationAccentColorManager.Apply(
            SystemCare.Helpers.CyberPalette.Accent, ApplicationTheme.Dark);
        if (settings.Current.Theme != "Dark")
        {
            settings.Current.Theme = "Dark";
            settings.Save();
        }

        bool minimized = e.Args.Contains("--minimized");

        // Animated splash continues the native splash (Assets/splash.png) while the app builds.
        SplashWindow? splash = null;
        if (!minimized)
        {
            splash = new SplashWindow();
            splash.Show();
            await Task.Delay(120); // let the splash paint before heavy init
        }

        var window = _services.GetRequiredService<MainWindow>();
        // The splash window was created first, so WPF auto-assigned it as Application.MainWindow.
        // Point MainWindow at the real window so the tray/single-instance restore targets it.
        MainWindow = window;
        var trayIcon = _services.GetRequiredService<ITrayIconService>();
        trayIcon.Initialize();

        // Keep the scheduled task in sync with the persisted setting on every launch.
        _services.GetRequiredService<IScheduledMaintenanceService>().Sync();
        // Keep the "start with Windows" logon task in sync (and refresh its exe path after upgrades).
        _services.GetRequiredService<IStartupLauncherService>().Sync();
        // If a previous Game Booster session didn't close cleanly, restore the system now (journal replay).
        _ = _services.GetRequiredService<IGameBoosterService>().RecoverIfInterruptedAsync();

        // Restore the live monitor (tray stats + mini-widget) if the user left them on.
        if (settings.Current.ShowTrayStats) trayIcon.EnableLiveStats(true);
        if (settings.Current.ShowMiniMonitor) _services.GetRequiredService<IMiniMonitorService>().Show();
        if (settings.Current.ResourceAlertsEnabled) _services.GetRequiredService<IResourceAlertService>().Start();

        if (minimized)
        {
            window.WindowState = WindowState.Minimized;
            return;
        }

        // Keep the splash up briefly so its animation is seen, then hand off to the main window.
        await Task.Delay(1100);
        window.Show();
        if (splash is not null)
            await splash.FadeOutAndCloseAsync();

        if (settings.Current.CheckForUpdatesOnStartup)
            _ = CheckForUpdatesAsync();
    }

    /// <summary>
    /// On startup: check the GitHub repo for a newer release, download its installer in the background,
    /// then offer to install it now (which closes the app so the installer can replace files).
    /// </summary>
    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var updates = _services.GetRequiredService<IUpdateService>();
            var snackbar = _services.GetRequiredService<ISnackbarService>();

            var info = await updates.CheckAsync();
            if (info is null) return; // already up to date / offline

            if (!info.HasAsset)
            {
                snackbar.Show("Update available",
                    $"SystemCare {info.Version} is on GitHub, but its release has no installer attached.",
                    Wpf.Ui.Controls.ControlAppearance.Info, null, TimeSpan.FromSeconds(8));
                return;
            }

            snackbar.Show("Downloading update",
                $"Getting SystemCare {info.Version} from GitHub…",
                Wpf.Ui.Controls.ControlAppearance.Info, null, TimeSpan.FromSeconds(4));

            string? installer = await updates.DownloadAsync(null, CancellationToken.None);
            if (installer is null)
            {
                snackbar.Show("Update download failed",
                    "Couldn't download the new version. You can retry from Settings → Updates.",
                    Wpf.Ui.Controls.ControlAppearance.Caution, null, TimeSpan.FromSeconds(7));
                return;
            }

            var choice = await _services.GetRequiredService<IContentDialogService>().ShowSimpleDialogAsync(
                new SimpleContentDialogCreateOptions
                {
                    Title = $"Update ready: SystemCare {info.Version}",
                    Content = "A newer version has been downloaded.\n\nInstall it now? SystemCare will close and the installer will open.",
                    PrimaryButtonText = "Install now",
                    CloseButtonText = "Later",
                });

            if (choice == Wpf.Ui.Controls.ContentDialogResult.Primary)
            {
                // Safety net before the installer replaces app files.
                if (await _services.GetRequiredService<IBackupConfirmationService>().ConfirmRestorePointAsync("updating SystemCare"))
                {
                    var (ok, msg) = await _services.GetRequiredService<IRestorePointService>()
                        .CreateRestorePointAsync("Before SystemCare app update");
                    if (!ok) _services.GetRequiredService<ILogService>().Warn("Updater", $"Restore point not created: {msg}");
                }

                // Only exit if the installer actually started — declining UAC must not close the app silently.
                if (updates.Launch(installer))
                {
                    if (Current.MainWindow is MainWindow main) main.ForceExit = true; // bypass minimize-to-tray
                    Current.Shutdown();
                }
                else
                {
                    snackbar.Show("Update not started",
                        "The installer didn't start. You can install it anytime from Settings → Updates.",
                        Wpf.Ui.Controls.ControlAppearance.Caution, null, TimeSpan.FromSeconds(7));
                }
            }
            else
            {
                snackbar.Show("Update ready",
                    $"SystemCare {info.Version} is in your Downloads folder — install it anytime from Settings → Updates.",
                    Wpf.Ui.Controls.ControlAppearance.Success, null, TimeSpan.FromSeconds(7));
            }
        }
        catch (Exception ex)
        {
            // offline / parse error / dialog host unavailable — never block startup over an update check
            try { _services.GetRequiredService<ILogService>().Warn("Updater", $"Startup update check failed: {ex.Message}"); }
            catch (Exception) { }
        }
    }

    /// <summary>Listens for a second instance's activation signal and restores the window.</summary>
    private void StartActivationListener()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _activateEvent.WaitOne();
                    Dispatcher.BeginInvoke(() =>
                    {
                        try { _services.GetRequiredService<ITrayIconService>().ShowMainWindow(); }
                        catch (Exception) { }
                    });
                }
                catch (Exception)
                {
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "SystemCare.ActivationListener",
        };
        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _services.GetRequiredService<IResourceAlertService>().Stop(); } catch (Exception) { }
        try { _services.GetRequiredService<IMiniMonitorService>().Shutdown(); } catch (Exception) { }
        try { _services.GetRequiredService<ITrayIconService>().Dispose(); } catch (Exception) { }
        try { _services.GetRequiredService<ITemperatureService>().Dispose(); } catch (Exception) { } // unload the sensor driver
        try { _services.GetRequiredService<INetworkUsageService>().Dispose(); } catch (Exception) { } // stop the ETW session
        try { _activateEvent?.Dispose(); } catch (Exception) { }
        try { _singleInstanceMutex?.Dispose(); } catch (Exception) { }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("UI", e.Exception);
        MessageBox.Show(e.Exception.Message, "SystemCare — Unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    // A faulted background thread; the runtime is usually tearing down, so we can only record it.
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => LogCrash("AppDomain", e.ExceptionObject as Exception);

    // A faulted Task that was never awaited/observed; mark it observed so it can't escalate.
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("Task", e.Exception);
        e.SetObserved();
    }

    /// <summary>Records an unhandled exception to the rolling log plus a last-resort temp-file marker.</summary>
    private static void LogCrash(string source, Exception? ex)
    {
        if (ex is null) return;
        try { _services.GetRequiredService<ILogService>().Error(source, "Unhandled exception", ex); }
        catch (Exception) { }
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "systemcare_error.log"), ex.ToString()); }
        catch (Exception) { }
    }
}
