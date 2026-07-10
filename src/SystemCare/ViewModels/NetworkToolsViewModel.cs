using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public partial class NetworkToolsViewModel : ObservableObject
{
    private const int MaxOutputChars = 40_000;

    private readonly INetworkToolsService _network;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<NetConnection> Connections { get; } = [];

    [ObservableProperty] private bool _isLoadingConnections;
    [ObservableProperty] private string _connectionSummary = "";
    [ObservableProperty] private string _target = "8.8.8.8";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _outputText = "Run a tool to see output here.";

    public NetworkToolsViewModel(INetworkToolsService network)
    {
        _network = network;
    }

    public async void OnNavigatedTo()
    {
        try
        {
            if (Connections.Count == 0) await RefreshConnectionsAsync();
        }
        catch (Exception)
        {
            // async void: an unhandled exception here would surface as a raw error dialog, so contain it.
        }
    }

    [RelayCommand]
    private void OpenNetMonitor()
    {
        if (Application.Current.MainWindow is MainWindow window)
            window.NavigateTo("NetMonitor");
    }

    [RelayCommand]
    private async Task RefreshConnectionsAsync()
    {
        IsLoadingConnections = true;
        try
        {
            var list = await Task.Run(_network.GetConnections);
            Connections.Clear();
            foreach (var c in list) Connections.Add(c);
            ConnectionSummary = $"{list.Count} active connections";
        }
        finally
        {
            IsLoadingConnections = false;
        }
    }

    [RelayCommand]
    private async Task PingAsync() => await RunToolAsync(ct => _network.PingAsync(Target, AppendLine, ct));

    [RelayCommand]
    private async Task TracerouteAsync() => await RunToolAsync(ct => _network.TracerouteAsync(Target, AppendLine, ct));

    [RelayCommand]
    private void FlushDns()
    {
        _output.Clear();
        AppendLine(_network.FlushDns());
        FlushOutput();
    }

    [RelayCommand]
    private async Task RenewIpAsync() => await RunToolAsync(ct => _network.RenewIpAsync(AppendLine, ct));

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunToolAsync(Func<CancellationToken, Task> action)
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _output.Clear();
        OutputText = "";
        try
        {
            await action(_cts.Token);
        }
        catch (Exception ex)
        {
            AppendLine($"Error: {ex.Message}");
        }
        finally
        {
            FlushOutput();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void AppendLine(string line)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _output.AppendLine(line);
            if (_output.Length > MaxOutputChars) _output.Remove(0, _output.Length - MaxOutputChars);
            OutputText = _output.ToString();
        });
    }

    private void FlushOutput() =>
        Application.Current?.Dispatcher.Invoke(() => OutputText = _output.ToString());
}
