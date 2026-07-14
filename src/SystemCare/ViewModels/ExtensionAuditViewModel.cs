using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public class ExtensionRowViewModel(BrowserExtensionInfo info)
{
    public string Browser { get; } = info.Browser;
    public string Name { get; } = info.Name;
    public string VersionText { get; } = string.IsNullOrEmpty(info.Version) ? "" : $"v{info.Version}";
    public string ProfileText { get; } = info.Profile == "Default" ? "" : info.Profile;
    public string RiskReason { get; } = info.RiskReason;
    public string RiskLabel { get; } = info.RiskLevel switch { 2 => "High reach", 1 => "Broad reach", _ => "Limited" };
    public bool IsHigh { get; } = info.RiskLevel == 2;
    public bool IsMedium { get; } = info.RiskLevel == 1;
    public bool IsLow { get; } = info.RiskLevel == 0;
    public string PermissionsText { get; } = info.Permissions.Count == 0
        ? "No special permissions"
        : string.Join(" · ", info.Permissions.Take(6)) + (info.Permissions.Count > 6 ? $" (+{info.Permissions.Count - 6})" : "");
}

public partial class ExtensionAuditViewModel : ObservableObject
{
    private readonly IBrowserExtensionService _extensions;

    public ObservableCollection<ExtensionRowViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "Scan to list every installed browser extension and its permission reach.";

    public ExtensionAuditViewModel(IBrowserExtensionService extensions) => _extensions = extensions;

    private bool _scannedOnce;

    public void OnNavigatedTo()
    {
        if (_scannedOnce) return;
        _scannedOnce = true;
        if (ScanCommand.CanExecute(null)) ScanCommand.Execute(null);
    }

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanScan), IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Reading extension manifests…";
        try
        {
            var found = await _extensions.ScanAsync(ct);
            Items.Clear();
            foreach (var ext in found) Items.Add(new ExtensionRowViewModel(ext));

            int high = found.Count(e => e.RiskLevel == 2);
            StatusText = found.Count == 0
                ? "No browser extensions found."
                : high > 0
                    ? $"{found.Count} extensions — {high} can see everything you browse. Review those in the browser's own extension manager."
                    : $"{found.Count} extensions — none with the riskiest permission combinations. Nice.";
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
}
