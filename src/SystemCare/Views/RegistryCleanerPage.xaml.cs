using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class RegistryCleanerPage : Page
{
    public RegistryCleanerPage(RegistryCleanerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
