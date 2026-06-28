using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class BenchmarkPage : Page
{
    private readonly BenchmarkViewModel _viewModel;

    public BenchmarkPage(BenchmarkViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, System.Windows.RoutedEventArgs e) => _viewModel.OnNavigatedTo();
}
