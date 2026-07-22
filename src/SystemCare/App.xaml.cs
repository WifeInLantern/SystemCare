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

    private static readonly IServiceProvider _services = ServiceRegistration.Build();

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;

    public static IServiceProvider Services => _services;


    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch faults off the UI thread too (the XAML wires only DispatcherUnhandledException). Without
        // these, an exception on a background thread or an unobserved Task can tear the process down with
        // nothing logged. Subscribed before the headless/interactive split so both run modes are covered.
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // --report [path] (2.17): headless Care Report export for scripts and schedulers.
        int reportIdx = Array.IndexOf(e.Args, "--report");
        if (reportIdx >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var reportLog = _services.GetRequiredService<ILogService>();
            try
            {
                string path = reportIdx + 1 < e.Args.Length && !e.Args[reportIdx + 1].StartsWith("--", StringComparison.Ordinal)
                    ? e.Args[reportIdx + 1]
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "SystemCare", $"SystemCare-report-{DateTime.Now:yyyy-MM-dd}.html");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await _services.GetRequiredService<ICareReportExporter>().ExportAsync(path);
                reportLog.Info("Report", $"Headless report exported: {path}");
            }
            catch (Exception ex)
            {
                reportLog.Error("Report", "Headless report export failed", ex);
            }
            finally
            {
                Shutdown();
            }
            return;
        }

        // Headless runs: no window, balloon, then exit.
        //   --run-maintenance             : the scheduled pass, using the profile from Settings.
        //   --clean/--trim-ram/--flush-dns/--empty-recycle-bin (2.14) : an explicit ad-hoc profile
        //     for scripts and power users, e.g.  SystemCare.exe --clean --trim-ram
        bool cliClean = e.Args.Contains("--clean");
        bool cliTrim = e.Args.Contains("--trim-ram");
        bool cliDns = e.Args.Contains("--flush-dns");
        bool cliBin = e.Args.Contains("--empty-recycle-bin");
        bool cliProfile = cliClean || cliTrim || cliDns || cliBin;
        if (e.Args.Contains("--run-maintenance") || cliProfile)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var log = _services.GetRequiredService<ILogService>();
            log.Info("Maintenance", cliProfile
                ? $"Headless CLI run started ({string.Join(' ', e.Args)})."
                : "Headless scheduled maintenance started (--run-maintenance).");
            var tray = _services.GetRequiredService<ITrayIconService>();
            tray.Initialize();
            try
            {
                var profile = cliProfile ? new MaintenanceProfile(cliClean, cliTrim, cliDns, cliBin) : null;
                var result = await _services.GetRequiredService<IScheduledMaintenanceService>().RunMaintenanceNowAsync(profile);
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
        // 2.16: user-selectable accent (Cyan default / Magenta / Violet) for accent-driven controls.
        SystemCare.Helpers.AccentThemes.Apply(settings.Current.AccentTheme);
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

        ScheduleBackgroundChecks();

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
    /// Background housekeeping (2.19.4): Autorun Guard, boot report, monthly Care Report and the
    /// restore-point watchdog all touch disk/WMI/registry. Fired inline they competed with the
    /// first frame the user is waiting for, and each release added another one. They now run
    /// sequentially at background priority a few seconds after the window is up — same work, off
    /// the cold-start critical path. Each service is individually best-effort and never throws.
    /// </summary>
    private static void ScheduleBackgroundChecks()
    {
        var startupDelay = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        startupDelay.Tick += async (_, _) =>
        {
            startupDelay.Stop();
            try
            {
                await _services.GetRequiredService<IAutorunGuardService>().CheckAsync();
                await _services.GetRequiredService<IBootHistoryService>().CheckAndReportAsync();
                await _services.GetRequiredService<IMonthlyReportService>().CheckAsync();
                await _services.GetRequiredService<IRestorePointWatchdogService>().CheckAsync();
            }
            catch (Exception)
            {
                // every check is best-effort by contract; startup must never fail because of them
            }
        };
        startupDelay.Start();

        // Autorun Guard periodic re-check: programs installed mid-session would otherwise go
        // unnoticed until the next launch. Snapshot-diff based and cheap.
        var guardTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromHours(6) };
        guardTimer.Tick += (_, _) => _ = _services.GetRequiredService<IAutorunGuardService>().CheckAsync();
        guardTimer.Start();
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
