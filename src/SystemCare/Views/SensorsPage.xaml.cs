using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class SensorsPage : Page
{
    private readonly SensorsViewModel _viewModel;

    public SensorsPage(SensorsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedTo();
    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedFrom();
}
