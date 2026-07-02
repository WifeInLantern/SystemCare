using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class NetworkMonitorPage : Page
{
    private readonly NetworkMonitorViewModel _viewModel;

    public NetworkMonitorPage(NetworkMonitorViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.StartMonitoring();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.StopMonitoring();
}
