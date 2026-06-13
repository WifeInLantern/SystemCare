using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class CleanupPage : Page
{
    public CleanupPage(CleanupViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
