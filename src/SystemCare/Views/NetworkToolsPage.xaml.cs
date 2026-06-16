using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class NetworkToolsPage : Page
{
    private readonly NetworkToolsViewModel _viewModel;

    public NetworkToolsPage(NetworkToolsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.OnNavigatedTo();
        _viewModel.StartMonitoring();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.StopMonitoring();

    private void OnOutputChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) box.ScrollToEnd();
    }
}
