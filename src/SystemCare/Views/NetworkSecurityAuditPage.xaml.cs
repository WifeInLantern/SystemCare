using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class NetworkSecurityAuditPage : Page
{
    private readonly NetworkSecurityAuditViewModel _viewModel;

    public NetworkSecurityAuditPage(NetworkSecurityAuditViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.OnNavigatedTo();
}
