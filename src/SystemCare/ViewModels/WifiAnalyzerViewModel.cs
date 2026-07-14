using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public class WifiNetworkRowViewModel(WifiNetworkInfo info)
{
    public string Ssid { get; } = info.Ssid;
    public string SignalText { get; } = $"{info.SignalPercent}%";
    public string DetailText { get; } = info.Channel > 0 ? $"ch {info.Channel} · {info.Band}" : info.Band;
    public bool IsStrong { get; } = info.SignalPercent >= 70;
    public bool IsWeak { get; } = info.SignalPercent < 40;
}

public partial class WifiAnalyzerViewModel : ObservableObject
{
    private readonly IWifiInfoService _wifi;

    public ObservableCollection<WifiNetworkRowViewModel> Nearby { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _ssid = "";
    [ObservableProperty] private int _signalPercent;
    [ObservableProperty] private string _connectionDetail = "";
    [ObservableProperty] private string _congestionText = "";
    [ObservableProperty] private string _statusText = "Reading Wi-Fi status…";

    public WifiAnalyzerViewModel(IWifiInfoService wifi) => _wifi = wifi;

    public void OnNavigatedTo()
    {
        if (RefreshCommand.CanExecute(null)) RefreshCommand.Execute(null);
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh), IncludeCancelCommand = true)]
    private async Task RefreshAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var report = await _wifi.GetReportAsync(ct);

            if (!report.WlanAvailable)
            {
                IsConnected = false;
                StatusText = "No wireless interface found (or the WLAN service isn't running).";
                Nearby.Clear();
                CongestionText = "";
                return;
            }

            if (report.Connection is { } c)
            {
                IsConnected = true;
                Ssid = c.Ssid;
                SignalPercent = c.SignalPercent;
                ConnectionDetail =
                    $"Channel {c.Channel} · {c.Band}" +
                    (string.IsNullOrEmpty(c.RadioType) ? "" : $" · {c.RadioType}") +
                    (c.ReceiveMbps > 0 ? $" · ↓{c.ReceiveMbps:0} / ↑{c.TransmitMbps:0} Mbps link" : "");
                CongestionText = report.SameChannelCount > 3
                    ? $"{report.SameChannelCount} networks share channel {c.Channel} — congestion may hurt speed; your router could pick a quieter channel."
                    : report.SameChannelCount > 0
                        ? $"{report.SameChannelCount} network(s) on channel {c.Channel} — looks uncongested."
                        : "";
            }
            else
            {
                IsConnected = false;
                CongestionText = "";
                StatusText = "Wireless is available but not connected (or the adapter reports no data).";
            }

            Nearby.Clear();
            foreach (var network in report.Nearby) Nearby.Add(new WifiNetworkRowViewModel(network));
            if (report.Connection is not null)
                StatusText = $"{Nearby.Count} network(s) in range.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Refresh cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
