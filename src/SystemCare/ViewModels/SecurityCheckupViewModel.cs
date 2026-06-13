using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class SecurityCheckupViewModel : ObservableObject
{
    private readonly ISecurityCheckService _security;

    public ObservableCollection<SecurityCheck> Checks { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _summaryText = "";

    public SecurityCheckupViewModel(ISecurityCheckService security)
    {
        _security = security;
    }

    public async void OnNavigatedTo()
    {
        if (Checks.Count == 0) await ScanAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            var checks = await _security.GetChecksAsync();
            Checks.Clear();
            foreach (var c in checks) Checks.Add(c);

            int issues = checks.Count(c => c.Status is SecurityStatus.Warning or SecurityStatus.Bad);
            SummaryText = issues == 0
                ? "Everything looks good — no security issues found."
                : $"{issues} item(s) need your attention.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Fix(SecurityCheck check)
    {
        if (!string.IsNullOrEmpty(check.FixTarget)) _security.OpenFix(check.FixTarget!);
    }
}
