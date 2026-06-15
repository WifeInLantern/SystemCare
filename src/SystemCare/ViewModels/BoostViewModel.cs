using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class BoostAppViewModel(int pid, string name, long ram)
{
    public int Pid { get; } = pid;
    public string Name { get; } = name;
    public string RamText { get; } = ByteFormatter.Format(ram);
    public bool IsSelected { get; set; }
}

public partial class BoostViewModel : ObservableObject
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "SystemCare", "Idle", "System", "csrss", "winlogon", "services", "lsass", "smss", "dwm",
    };

    private readonly IBoostService _boost;
    private readonly IProcessService _processes;
    private readonly IHistoryService _history;

    public ObservableCollection<BoostAppViewModel> Apps { get; } = [];

    [ObservableProperty] private bool _isBoosted;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Boost switches to High Performance, frees memory, and (optionally) pauses background apps.";

    public BoostViewModel(IBoostService boost, IProcessService processes, IHistoryService history)
    {
        _boost = boost;
        _processes = processes;
        _history = history;
        IsBoosted = boost.IsBoosted;
    }

    public void OnNavigatedTo()
    {
        if (Apps.Count > 0) return;
        int self = Environment.ProcessId;
        // Candidate "background apps" = user apps with a visible window, excluding self + system.
        var candidates = _processes.GetProcesses()
            .Where(p => !string.IsNullOrWhiteSpace(p.Title) && p.Pid != self && !ProtectedNames.Contains(p.Name))
            .DistinctBy(p => p.Name)
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(20);
        foreach (var p in candidates)
            Apps.Add(new BoostAppViewModel(p.Pid, p.Name, p.WorkingSetBytes));
    }

    [RelayCommand]
    private async Task BoostAsync()
    {
        IsBusy = true;
        try
        {
            var pids = Apps.Where(a => a.IsSelected).Select(a => a.Pid).ToList();
            var result = await _boost.BoostAsync(pids);
            IsBoosted = true;
            StatusText = $"Boost active — power plan: {result.PowerPlanName}, freed {ByteFormatter.Format(result.BytesFreed)}" +
                         (result.AppsPaused > 0 ? $", paused {result.AppsPaused} app(s)." : ".");
            _history.Record("Boost",
                $"High Performance · freed {ByteFormatter.Format(result.BytesFreed)} of RAM" +
                (result.AppsPaused > 0 ? $" · paused {result.AppsPaused} app(s)" : ""),
                result.BytesFreed, result.AppsPaused, "Rocket24");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _boost.RestoreAsync();
            IsBoosted = false;
            StatusText = $"Restored — power plan back to {result.PowerPlanName}, paused apps resumed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
