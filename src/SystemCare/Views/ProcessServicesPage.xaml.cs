using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class ProcessServicesPage : Page
{
    private readonly ProcessServicesViewModel _viewModel;

    public ProcessServicesPage(ProcessServicesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedTo();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedFrom();
}
