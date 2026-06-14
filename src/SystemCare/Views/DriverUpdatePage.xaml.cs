using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class DriverUpdatePage : Page
{
    private readonly DriverUpdateViewModel _viewModel;

    public DriverUpdatePage(DriverUpdateViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedTo();
}
