using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class PrivacyItemViewModel(PrivacyCategory category) : ObservableObject
{
    public PrivacyCategory Category { get; } = category;
    public string Name => Category.Name;
    public string Description => Category.Description;

    [ObservableProperty] private bool _isSelected = category.EnabledByDefault;
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private bool _isBlocked;
}

public partial class PrivacyGroupViewModel(string name)
{
    public string Name { get; } = name;
    public ObservableCollection<PrivacyItemViewModel> Items { get; } = [];
}

public partial class RunningBrowserViewModel(string displayName, string processName, PrivacyViewModel owner) : ObservableObject
{
    public string DisplayName { get; } = displayName;
    public string ProcessName { get; } = processName;
    public string Message => $"{DisplayName} is running — its data is locked and will be skipped.";

    [RelayCommand]
    private void Close() => owner.CloseBrowser(ProcessName);
}

public partial class PrivacyViewModel : ObservableObject
{
    private static readonly Dictionary<string, string> BrowserNames = new()
    {
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Mozilla Firefox",
    };

    private readonly IPrivacyCleanerService _privacy;
    private readonly ISnackbarService _snackbar;

    public ObservableCollection<PrivacyGroupViewModel> Groups { get; } = [];
    public ObservableCollection<RunningBrowserViewModel> RunningBrowsers { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Scan to see what privacy traces can be cleared.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CleanCommand))] private bool _canClean;

    public PrivacyViewModel(IPrivacyCleanerService privacy, ISnackbarService snackbar)
    {
        _privacy = privacy;
        _snackbar = snackbar;

        foreach (var group in privacy.Categories.GroupBy(c => c.Group))
        {
            var groupVm = new PrivacyGroupViewModel(group.Key);
            foreach (var category in group)
                groupVm.Items.Add(new PrivacyItemViewModel(category));
            Groups.Add(groupVm);
        }
    }

    private IEnumerable<PrivacyItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    public void CloseBrowser(string processName)
    {
        _privacy.TryCloseBrowser(processName);
        // Browsers shut down asynchronously; re-check shortly after asking.
        _ = Task.Delay(1500).ContinueWith(_ => System.Windows.Application.Current.Dispatcher.Invoke(RefreshRunningBrowsers));
    }

    private void RefreshRunningBrowsers()
    {
        var running = _privacy.GetRunningBrowsers();
        RunningBrowsers.Clear();
        foreach (var name in running)
            RunningBrowsers.Add(new RunningBrowserViewModel(BrowserNames.GetValueOrDefault(name, name), name, this));

        foreach (var item in AllItems)
            item.IsBlocked = item.Category.BrowserProcess is string p && running.Contains(p);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsBusy = true;
        CanClean = false;
        try
        {
            RefreshRunningBrowsers();
            var statuses = await _privacy.ScanAsync(ct);

            long total = 0;
            foreach (var status in statuses)
            {
                var item = AllItems.FirstOrDefault(i => i.Category.Id == status.Category.Id);
                if (item is null) continue;
                item.IsBlocked = status.BlockedByRunningBrowser;
                item.SizeText = status.Category.Kind switch
                {
                    PrivacyKind.Files or PrivacyKind.DirectoryContents =>
                        status.ItemCount == 0 ? "nothing found" : $"{ByteFormatter.Format(status.Bytes)} · {status.ItemCount:N0} items",
                    PrivacyKind.RegistryValues =>
                        status.ItemCount == 0 ? "empty" : $"{status.ItemCount:N0} entries",
                    _ => "ready to clear",
                };
                total += status.Bytes;
            }

            StatusText = $"Scan complete — about {ByteFormatter.Format(total)} of privacy traces found.";
            CanClean = true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        IsBusy = true;
        try
        {
            var ids = AllItems.Where(i => i.IsSelected && !i.IsBlocked).Select(i => i.Category.Id).ToList();
            var result = await _privacy.CleanAsync(ids, CancellationToken.None);

            int blockedCount = AllItems.Count(i => i.IsSelected && i.IsBlocked);
            StatusText = $"Cleared {result.ItemsRemoved:N0} items ({ByteFormatter.Format(result.BytesRemoved)})." +
                         (result.ItemsSkipped > 0 ? $" {result.ItemsSkipped:N0} locked items skipped." : "") +
                         (blockedCount > 0 ? $" {blockedCount} categories skipped because a browser is running." : "");

            _snackbar.Show("Privacy traces cleared",
                $"Removed {result.ItemsRemoved:N0} items ({ByteFormatter.Format(result.BytesRemoved)}).",
                ControlAppearance.Success, null, TimeSpan.FromSeconds(5));

            foreach (var item in AllItems) item.SizeText = "";
            CanClean = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void OnNavigatedTo() => RefreshRunningBrowsers();
}
