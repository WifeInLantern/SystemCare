using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class GameModeViewModel : ObservableObject
{
    // Background apps worth pausing during a game/focus session (matched against process names).
    private static readonly HashSet<string> CommonBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave", "Discord", "Spotify", "Slack", "Teams",
        "OneDrive", "Dropbox", "Steam", "EpicGamesLauncher", "Skype", "Telegram", "WhatsApp",
    };

    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "SystemCare", "Idle", "System", "csrss", "winlogon", "services", "lsass", "smss", "dwm",
    };

    private readonly IGameModeService _gameMode;
    private readonly IProcessService _processes;
    private readonly IHistoryService _history;

    public ObservableCollection<BoostAppViewModel> Apps { get; } = [];

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _silenceNotifications = true;
    [ObservableProperty] private string _statusText =
        "Game Mode switches to High Performance, frees memory, pauses background apps, and (optionally) silences notifications. Exit to restore everything.";

    public GameModeViewModel(IGameModeService gameMode, IProcessService processes, IHistoryService history)
    {
        _gameMode = gameMode;
        _processes = processes;
        _history = history;
        IsActive = gameMode.IsActive;
    }

    public void OnNavigatedTo()
    {
        if (Apps.Count > 0) return;
        int self = Environment.ProcessId;
        var candidates = _processes.GetProcesses()
            .Where(p => p.Pid != self && !ProtectedNames.Contains(p.Name)
                        && (!string.IsNullOrWhiteSpace(p.Title) || CommonBackground.Contains(p.Name)))
            .DistinctBy(p => p.Name)
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(24);
        foreach (var p in candidates)
            Apps.Add(new BoostAppViewModel(p.Pid, p.Name, p.WorkingSetBytes)
            {
                // Pre-select the well-known background talkers.
                IsSelected = CommonBackground.Contains(p.Name),
            });
    }

    [RelayCommand]
    private async Task EnterAsync()
    {
        IsBusy = true;
        try
        {
            var pids = Apps.Where(a => a.IsSelected).Select(a => a.Pid).ToList();
            var result = await _gameMode.EnterAsync(pids, SilenceNotifications);
            IsActive = true;
            StatusText = $"Game Mode active — {result.PowerPlanName}, freed {ByteFormatter.Format(result.BytesFreed)}" +
                         (result.AppsPaused > 0 ? $", paused {result.AppsPaused} app(s)" : "") +
                         (result.NotificationsSilenced ? ", notifications silenced." : ".");
            _history.Record("Game Mode",
                $"High Performance · freed {ByteFormatter.Format(result.BytesFreed)} of RAM" +
                (result.AppsPaused > 0 ? $" · paused {result.AppsPaused} app(s)" : ""),
                result.BytesFreed, result.AppsPaused, "Flash24");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _gameMode.ExitAsync();
            IsActive = false;
            StatusText = $"Exited Game Mode — power plan back to {result.PowerPlanName}, apps resumed and notifications restored.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
