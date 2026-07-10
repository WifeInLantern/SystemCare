using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class ProcessRowViewModel(ProcessEntry entry)
{
    public ProcessEntry Entry { get; } = entry;
    public int Pid => Entry.Pid;
    public string Name => Entry.Name;
    public string Title => string.IsNullOrWhiteSpace(Entry.Title) ? Entry.Name : Entry.Title;
    public string RamText => ByteFormatter.Format(Entry.WorkingSetBytes);
    public string CpuText => $"{Entry.CpuPercent:0.0}%";
}

public partial class ServiceRowViewModel(ServiceEntry entry) : ObservableObject
{
    public ServiceEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string DisplayName => Entry.DisplayName;
    public string StartMode => Entry.StartMode;

    [ObservableProperty] private ServiceState _state = entry.State;
    public bool IsRunning => State == ServiceState.Running;
    public string StateText => State.ToString();

    partial void OnStateChanged(ServiceState value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StateText));
    }
}

public partial class ProcessServicesViewModel : ObservableObject
{
    private readonly IProcessService _processes;
    private readonly IServiceControlService _services;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly DispatcherTimer _timer;
    private List<ServiceRowViewModel> _allServices = [];

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = [];
    public ObservableCollection<ServiceRowViewModel> Services { get; } = [];

    [ObservableProperty] private string _processSummary = "";
    [ObservableProperty] private string _serviceSearch = "";
    [ObservableProperty] private bool _isServicesBusy;

    public ProcessServicesViewModel(
        IProcessService processes,
        IServiceControlService services,
        ISnackbarService snackbar,
        IContentDialogService dialogs)
    {
        _processes = processes;
        _services = services;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshProcesses();
    }

    partial void OnServiceSearchChanged(string value) => ApplyServiceFilter();

    public async void OnNavigatedTo()
    {
        try
        {
            RefreshProcesses();
            _timer.Start();
            if (_allServices.Count == 0) await RefreshServicesAsync();
        }
        catch (Exception)
        {
            // async void: an unhandled exception here would surface as a raw error dialog, so contain it.
        }
    }

    public void OnNavigatedFrom() => _timer.Stop();

    private void RefreshProcesses()
    {
        var list = _processes.GetProcesses();
        // Preserve scroll/selection feel by replacing in place.
        Processes.Clear();
        foreach (var p in list.Take(200)) Processes.Add(new ProcessRowViewModel(p));
        ProcessSummary = $"{list.Count} processes · {ByteFormatter.Format(list.Sum(p => p.WorkingSetBytes))} working set";
    }

    [RelayCommand]
    private async Task EndProcessAsync(ProcessRowViewModel item)
    {
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = $"End {item.Name}?",
            Content = $"PID {item.Pid}. Ending a process can cause unsaved work to be lost.",
            PrimaryButtonText = "End task",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        if (_processes.EndProcess(item.Pid))
        {
            Processes.Remove(item);
            _snackbar.Show("Process ended", $"{item.Name} (PID {item.Pid}) was terminated.",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
        }
        else
        {
            _snackbar.Show("Could not end process", $"{item.Name} is protected or already exited.",
                ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
        }
    }

    [RelayCommand]
    private async Task RefreshServicesAsync()
    {
        IsServicesBusy = true;
        try
        {
            var list = await _services.GetServicesAsync();
            _allServices = list.Select(s => new ServiceRowViewModel(s)).ToList();
            ApplyServiceFilter();
        }
        finally
        {
            IsServicesBusy = false;
        }
    }

    private void ApplyServiceFilter()
    {
        IEnumerable<ServiceRowViewModel> filtered = _allServices;
        if (!string.IsNullOrWhiteSpace(ServiceSearch))
        {
            string term = ServiceSearch.Trim();
            filtered = filtered.Where(s =>
                s.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        Services.Clear();
        foreach (var s in filtered) Services.Add(s);
    }

    [RelayCommand]
    private async Task ToggleServiceAsync(ServiceRowViewModel item)
    {
        IsServicesBusy = true;
        try
        {
            bool ok = item.IsRunning
                ? await _services.StopAsync(item.Name)
                : await _services.StartAsync(item.Name);

            if (ok)
            {
                item.State = item.IsRunning ? ServiceState.Stopped : ServiceState.Running;
                // Re-read true status shortly after.
                await RefreshServicesAsync();
            }
            else
            {
                _snackbar.Show("Service action failed",
                    $"Could not {(item.IsRunning ? "stop" : "start")} \"{item.DisplayName}\".",
                    ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));
            }
        }
        finally
        {
            IsServicesBusy = false;
        }
    }
}
