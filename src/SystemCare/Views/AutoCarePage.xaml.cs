using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class AutoCarePage : Page
{
    public AutoCarePage(AutoCareViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
