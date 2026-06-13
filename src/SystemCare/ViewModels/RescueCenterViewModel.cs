using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SystemCare.ViewModels;

public partial class RescueCenterViewModel : ObservableObject
{
    private readonly IRestorePointService _restore;
    private readonly ISnackbarService _snackbar;

    public ObservableCollection<RestorePoint> Points { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Restore points let you roll Windows back to an earlier state if something goes wrong.";

    public RescueCenterViewModel(IRestorePointService restore, ISnackbarService snackbar)
    {
        _restore = restore;
        _snackbar = snackbar;
    }

    public async void OnNavigatedTo()
    {
        if (Points.Count == 0) await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var points = await _restore.GetRestorePointsAsync();
            Points.Clear();
            foreach (var p in points) Points.Add(p);
            StatusText = Points.Count == 0
                ? "No restore points yet. Create one before making big changes, or turn on System Protection in Windows."
                : $"{Points.Count} restore point(s) available.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        IsBusy = true;
        StatusText = "Creating a restore point…";
        try
        {
            var (ok, message) = await _restore.CreateRestorePointAsync($"SystemCare — {DateTime.Now:g}");
            _snackbar.Show(ok ? "Restore point created" : "Could not create restore point", message,
                ok ? ControlAppearance.Success : ControlAppearance.Caution, null, TimeSpan.FromSeconds(6));
            StatusText = message;
            if (ok) await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSystemRestore() => _restore.OpenSystemRestore();
}
