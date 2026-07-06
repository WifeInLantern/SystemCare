using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class ScheduledTaskItemViewModel : ObservableObject
{
    public ScheduledTaskInfo Info { get; }
    public ScheduledTaskItemViewModel(ScheduledTaskInfo info)
    {
        Info = info;
        _enabled = info.Enabled;
    }

    public string Name => Info.Name;
    public string Folder => Info.Folder;
    public string Author => string.IsNullOrWhiteSpace(Info.Author) ? "Unknown publisher" : Info.Author;
    [ObservableProperty] private bool _enabled;
    public string ToggleLabel => Enabled ? "Disable" : "Enable";

    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(ToggleLabel));
}

public partial class ScheduledTasksViewModel : ObservableObject
{
    private readonly IScheduledTaskManagerService _tasks;

    public ObservableCollection<ScheduledTaskItemViewModel> Tasks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    public ScheduledTasksViewModel(IScheduledTaskManagerService tasks) => _tasks = tasks;

    public async void OnNavigatedTo()
    {
        if (Tasks.Count == 0) await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Reading scheduled tasks…";
        try
        {
            var found = await _tasks.ListAsync(CancellationToken.None);
            Tasks.Clear();
            foreach (var t in found) Tasks.Add(new ScheduledTaskItemViewModel(t));
            StatusText = $"{found.Count} third-party task(s). Disabling stops a task from running automatically; it stays installed.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleAsync(ScheduledTaskItemViewModel? item)
    {
        if (item is null) return;
        bool target = !item.Enabled;
        var (ok, message) = await _tasks.SetEnabledAsync(item.Info.Path, target);
        if (ok) item.Enabled = target;
        StatusText = message;
    }

    private bool NotBusy() => !IsBusy;
}
