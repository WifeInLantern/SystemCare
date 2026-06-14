using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SystemCare.Services;
using SystemCare.ViewModels;
using SystemCare.Views;
using Wpf.Ui;
using Wpf.Ui.Appearance;

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

        // Domain services
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IJunkScanService, JunkScanService>();
        services.AddSingleton<IMemoryOptimizerService, MemoryOptimizerService>();
        services.AddSingleton<IHealthScoreService, HealthScoreService>();
        services.AddSingleton<IStartupManagerService, StartupManagerService>();
        services.AddSingleton<IPrivacyCleanerService, PrivacyCleanerService>();
        services.AddSingleton<IDiskAnalyzerService, DiskAnalyzerService>();
        services.AddSingleton<IDuplicateFinderService, DuplicateFinderService>();
        services.AddSingleton<IFileOperationService, FileOperationService>();
        services.AddSingleton<IHardwareInfoService, HardwareInfoService>();
        services.AddSingleton<IInstalledAppsService, InstalledAppsService>();
        services.AddSingleton<ILeftoverScanService, LeftoverScanService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IServiceControlService, ServiceControlService>();
        services.AddSingleton<IScheduledMaintenanceService, ScheduledMaintenanceService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IDiskMaintenanceService, DiskMaintenanceService>();
        services.AddSingleton<IRestorePointService, RestorePointService>();
        services.AddSingleton<IRegistryCleanerService, RegistryCleanerService>();
        services.AddSingleton<IEmptyFolderService, EmptyFolderService>();
        services.AddSingleton<IDeepCleanupService, DeepCleanupService>();
        services.AddSingleton<IAppPackageService, AppPackageService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ISecurityCheckService, SecurityCheckService>();
        services.AddSingleton<INetworkToolsService, NetworkToolsService>();
        services.AddSingleton<IPowerPlanService, PowerPlanService>();
        services.AddSingleton<ITweaksService, TweaksService>();
        services.AddSingleton<IBoostService, BoostService>();
        services.AddSingleton<IFileShredderService, FileShredderService>();
        services.AddSingleton<IDriverUpdateService, DriverUpdateService>();

        // Window
        services.AddSingleton<MainWindow>();

        // ViewModels — singletons so page state (scan results, timers) survives navigation
        services.AddSingleton<DashboardViewModel>();
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
        services.AddSingleton<SecurityCheckupViewModel>();
        services.AddSingleton<NetworkToolsViewModel>();
        services.AddSingleton<WindowsTweaksViewModel>();
        services.AddSingleton<BoostViewModel>();
        services.AddSingleton<FileShredderViewModel>();
        services.AddSingleton<DriverUpdateViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Pages
        services.AddTransient<DashboardPage>();
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
        services.AddTransient<SecurityCheckupPage>();
        services.AddTransient<NetworkToolsPage>();
        services.AddTransient<WindowsTweaksPage>();
        services.AddTransient<BoostPage>();
        services.AddTransient<FileShredderPage>();
        services.AddTransient<DriverUpdatePage>();
        services.AddTransient<SettingsPage>();

        return services.BuildServiceProvider();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless scheduled-maintenance run: no window, balloon, then exit.
        if (e.Args.Contains("--run-maintenance"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var tray = _services.GetRequiredService<ITrayIconService>();
            tray.Initialize();
            try
            {
                var result = await _services.GetRequiredService<IScheduledMaintenanceService>().RunMaintenanceNowAsync();
                tray.ShowBalloon("SystemCare maintenance complete",
                    $"Removed {Helpers.ByteFormatter.Format(result.BytesRemoved)} of junk and freed {Helpers.ByteFormatter.Format(result.BytesFreed)} of RAM.");
                await Task.Delay(TimeSpan.FromSeconds(6));
            }
            catch (Exception) { }
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
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        ApplicationAccentColorManager.Apply(
            System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF), ApplicationTheme.Dark);
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

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await _services.GetRequiredService<IUpdateService>().CheckAsync();
            if (update is not null)
            {
                _services.GetRequiredService<ISnackbarService>().Show(
                    "Update available",
                    $"SystemCare {update.Version} is available. Open Settings → Updates to download.",
                    Wpf.Ui.Controls.ControlAppearance.Info, null, TimeSpan.FromSeconds(8));
            }
        }
        catch (Exception) { }
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
        try { _services.GetRequiredService<ITrayIconService>().Dispose(); } catch (Exception) { }
        try { _activateEvent?.Dispose(); } catch (Exception) { }
        try { _singleInstanceMutex?.Dispose(); } catch (Exception) { }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "systemcare_error.log"), e.Exception.ToString());
        }
        catch (Exception) { }
        MessageBox.Show(e.Exception.Message, "SystemCare — Unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
