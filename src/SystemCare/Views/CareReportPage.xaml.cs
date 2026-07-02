using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class CareReportPage : Page
{
    private readonly CareReportViewModel _viewModel;

    public CareReportPage(CareReportViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedTo();
}
