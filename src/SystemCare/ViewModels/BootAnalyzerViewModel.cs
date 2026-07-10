using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class BootAnalyzerViewModel : ObservableObject
{
    private readonly IBootPerformanceService _boot;

    public ObservableCollection<StartupImpact> Apps { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _hasBootData;
    [ObservableProperty] private string _lastBootText = "—";
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _bootDurationText = "—";
    [ObservableProperty] private string _summaryText = "Reading boot performance…";

    public BootAnalyzerViewModel(IBootPerformanceService boot) => _boot = boot;

    public async void OnNavigatedTo()
    {
        try
        {
            if (!Loaded) await RefreshAsync();
        }
        catch (Exception)
        {
            // async void: an unhandled exception here would surface as a raw error dialog, so contain it.
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _boot.GetAsync();
            LastBootText = r.LastBootText;
            UptimeText = r.UptimeText;
            BootDurationText = r.BootDurationText;
            HasBootData = r.HasBootData;

            Apps.Clear();
            foreach (var a in r.Apps.OrderByDescending(a => a.DurationMs)) Apps.Add(a);

            SummaryText = r.HasBootData
                ? $"Your last boot took {r.BootDurationText}. The apps and services below added the most delay."
                : "Boot timing comes from the Diagnostics-Performance event log, which appears to be off on this PC. Uptime and last-boot time are still shown above.";
            Loaded = true;
        }
        finally { IsBusy = false; }
    }

    private bool NotBusy() => !IsBusy;
}
