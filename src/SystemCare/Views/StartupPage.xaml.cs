using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class StartupPage : Page
{
    private readonly StartupViewModel _viewModel;
    private bool _loadedOnce;

    public StartupPage(StartupViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        if (_viewModel.RefreshCommand.CanExecute(null))
            _viewModel.RefreshCommand.Execute(null);
    }
}
