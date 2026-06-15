using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class SoftwareUpdatePage : Page
{
    private readonly SoftwareUpdateViewModel _viewModel;

    public SoftwareUpdatePage(SoftwareUpdateViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedTo();
}
