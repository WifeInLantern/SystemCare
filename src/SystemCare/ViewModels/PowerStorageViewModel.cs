using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public class PageFileRowViewModel(PageFileInfo info)
{
    public string Path { get; } = info.Path;
    public string AllocatedText { get; } = ByteFormatter.Format(info.AllocatedBytes);
    public string InUseText { get; } = ByteFormatter.Format(info.InUseBytes);
}

public partial class PowerStorageViewModel : ObservableObject
{
    private readonly IPowerStorageAdvisorService _advisor;

    public ObservableCollection<PageFileRowViewModel> PageFiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisableHibernationCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableHibernationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetReducedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisableHibernationCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableHibernationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetReducedCommand))]
    private bool _hibernationEnabled;

    [ObservableProperty] private string _hibernationText = "";
    [ObservableProperty] private string _pageFileSummary = "";
    [ObservableProperty] private string _statusText = "";

    public PowerStorageViewModel(IPowerStorageAdvisorService advisor) => _advisor = advisor;

    public async void OnNavigatedTo()
    {
        try { await RefreshAsync(); }
        catch (Exception) { /* contain async-void faults; page just shows stale text */ }
    }

    private async Task RefreshAsync()
    {
        var status = await _advisor.GetStatusAsync();

        HibernationEnabled = status.HibernationEnabled;
        HibernationText = status.HibernationEnabled
            ? $"Hibernation is ON — hiberfil.sys reserves {ByteFormatter.Format(status.HiberfilBytes)} on the system drive. " +
              "It also powers Fast Startup. \"Reduced\" keeps Fast Startup at roughly half the size; " +
              "disabling reclaims all of it (you lose hibernate + Fast Startup, both reversible)."
            : "Hibernation is OFF — hiberfil.sys is not taking any space. Enable it to get hibernate and Fast Startup back.";

        PageFiles.Clear();
        foreach (var pf in status.PageFiles) PageFiles.Add(new PageFileRowViewModel(pf));
        PageFileSummary = status.PageFiles.Count == 0
            ? "No page file information available (or no page file configured)."
            : $"Page file(s) reserve {ByteFormatter.Format(status.PageFilesTotalBytes)}. This is virtual-memory backing — " +
              "shown for awareness; SystemCare leaves sizing to Windows, which manages it well on modern systems.";
    }

    private bool CanDisable() => !IsBusy && HibernationEnabled;
    private bool CanEnable() => !IsBusy && !HibernationEnabled;
    private bool CanReduce() => !IsBusy && HibernationEnabled;

    [RelayCommand(CanExecute = nameof(CanDisable))]
    private Task DisableHibernationAsync() => RunAsync(_advisor.DisableHibernationAsync);

    [RelayCommand(CanExecute = nameof(CanEnable))]
    private Task EnableHibernationAsync() => RunAsync(_advisor.EnableHibernationAsync);

    [RelayCommand(CanExecute = nameof(CanReduce))]
    private Task SetReducedAsync() => RunAsync(_advisor.SetReducedHibernationAsync);

    private async Task RunAsync(Func<Task<(bool Ok, string Message)>> action)
    {
        IsBusy = true;
        try
        {
            var (_, message) = await action();
            StatusText = message;
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
