using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class LargeFilesPage : Page
{
    private readonly LargeFilesViewModel _viewModel;

    public LargeFilesPage(LargeFilesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) { }
}
