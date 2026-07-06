using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class SpeedTestViewModel : ObservableObject
{
    private readonly ISpeedTestService _speed;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty] private string _downloadText = "—";
    [ObservableProperty] private string _uploadText = "—";
    [ObservableProperty] private string _latencyText = "—";
    [ObservableProperty] private string _statusText = "Press Start to measure your connection speed.";

    public SpeedTestViewModel(ISpeedTestService speed) => _speed = speed;

    [RelayCommand(CanExecute = nameof(NotRunning))]
    private async Task RunAsync()
    {
        IsRunning = true;
        DownloadText = UploadText = LatencyText = "…";
        _cts = new CancellationTokenSource();
        try
        {
            void Status(string s) => Application.Current?.Dispatcher.Invoke(() => StatusText = s);
            var r = await _speed.RunAsync(Status, _cts.Token);
            if (r.Ok)
            {
                DownloadText = $"{r.DownloadMbps:0.0} Mbps";
                UploadText = $"{r.UploadMbps:0.0} Mbps";
                LatencyText = $"{r.LatencyMs} ms";
                StatusText = $"Down {r.DownloadMbps:0.0} • Up {r.UploadMbps:0.0} Mbps • {r.LatencyMs} ms ping (via Cloudflare).";
            }
            else
            {
                DownloadText = UploadText = LatencyText = "—";
                StatusText = r.Message;
            }
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool NotRunning() => !IsRunning;
}
