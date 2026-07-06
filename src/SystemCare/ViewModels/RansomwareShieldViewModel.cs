using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class RansomwareShieldViewModel : ObservableObject
{
    private readonly IRansomwareShieldService _shield;

    public ObservableCollection<string> ProtectedFolders { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private string _headline = "Reading ransomware protection status…";
    [ObservableProperty] private string _statusIcon = "ShieldCheckmark24";
    [ObservableProperty] private string _toggleLabel = "Turn on";

    public RansomwareShieldViewModel(IRansomwareShieldService shield) => _shield = shield;

    public async void OnNavigatedTo() => await RefreshAsync();

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var s = await _shield.GetStatusAsync();
            IsAvailable = s.IsAvailable;
            IsOn = s.IsOn;
            Headline = s.Headline;
            StatusIcon = s.Icon;
            ToggleLabel = s.IsOn ? "Turn off" : "Turn on";
            ProtectedFolders.Clear();
            foreach (var f in s.ProtectedFolders) ProtectedFolders.Add(f);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            var (_, message) = await _shield.SetEnabledAsync(!IsOn);
            Headline = message;
            await RefreshAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenSecurity() => _shield.OpenWindowsSecurity();

    private bool NotBusy() => !IsBusy;
    private bool CanToggle() => !IsBusy && IsAvailable;
}
