using Microsoft.Extensions.DependencyInjection;
using SystemCare.Services;
using SystemCare.Services.GameBooster;
using SystemCare.ViewModels;
using SystemCare.Views;
using Wpf.Ui;

namespace SystemCare;

/// <summary>
/// Composition root (2.19.4). Extracted from App.xaml.cs, which had grown to 566 lines doing DI,
/// startup orchestration, CLI verbs, single-instance, splash, updates and fault handling at once.
/// Registrations live here; App.xaml.cs keeps only lifecycle. No behavior change.
/// </summary>
internal static class ServiceRegistration
{
    public static IServiceProvider Build()
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

        // 2.14 feature services
        services.AddSingleton<IAutorunGuardService, AutorunGuardService>();
        services.AddSingleton<IDriveTrendService, DriveTrendService>();
        services.AddSingleton<IAppCacheService, AppCacheService>();

        // 2.16 feature services
        services.AddSingleton<IBootHistoryService, BootHistoryService>();
        services.AddSingleton<IMonthlyReportService, MonthlyReportService>();
        services.AddSingleton<IPowerStorageAdvisorService, PowerStorageAdvisorService>();
        services.AddSingleton<ISearchIndexService, SearchIndexService>();

        // 2.17 feature services
        services.AddSingleton<IBrowserExtensionService, BrowserExtensionService>();
        services.AddSingleton<IWifiInfoService, WifiInfoService>();

        // 2.19 feature services
        services.AddSingleton<IRestorePointWatchdogService, RestorePointWatchdogService>();

        // Window
        services.AddSingleton<MainWindow>();

        // ViewModels — singletons so page state (scan results, timers) survives navigation
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<AutoCareViewModel>();
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<AppCachesViewModel>();
        services.AddSingleton<PowerStorageViewModel>();
        services.AddSingleton<ExtensionAuditViewModel>();
        services.AddSingleton<WifiAnalyzerViewModel>();
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
        services.AddTransient<AppCachesPage>();
        services.AddTransient<PowerStoragePage>();
        services.AddTransient<ExtensionAuditPage>();
        services.AddTransient<WifiAnalyzerPage>();
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
}
