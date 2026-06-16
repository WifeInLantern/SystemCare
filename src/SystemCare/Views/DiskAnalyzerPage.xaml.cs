using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class DiskAnalyzerPage : Page
{
    public DiskAnalyzerPage(DiskAnalyzerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
