using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class DnsViewModel : ObservableObject
{
    private readonly IDnsService _dns;

    public ObservableCollection<string> Adapters { get; } = [];
    public IReadOnlyList<DnsProvider> Providers => _dns.Providers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _selectedAdapter;
    [ObservableProperty] private string _currentDnsText = "—";
    [ObservableProperty] private string _statusText = "";

    public DnsViewModel(IDnsService dns) => _dns = dns;

    public void OnNavigatedTo()
    {
        if (Adapters.Count == 0) Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Adapters.Clear();
        foreach (var a in _dns.GetActiveAdapters()) Adapters.Add(a);
        SelectedAdapter ??= Adapters.FirstOrDefault();
        UpdateCurrent();
    }

    partial void OnSelectedAdapterChanged(string? value) => UpdateCurrent();

    private void UpdateCurrent()
    {
        CurrentDnsText = string.IsNullOrEmpty(SelectedAdapter) ? "—" : _dns.GetCurrentDns(SelectedAdapter!);
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ApplyAsync(DnsProvider? provider)
    {
        if (provider is null || string.IsNullOrEmpty(SelectedAdapter)) return;
        IsBusy = true;
        StatusText = $"Applying {provider.Name} to {SelectedAdapter}…";
        try
        {
            var (ok, message) = await _dns.ApplyAsync(SelectedAdapter!, provider, CancellationToken.None);
            StatusText = message;
            UpdateCurrent();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool NotBusy() => !IsBusy;
}
