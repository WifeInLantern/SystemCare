using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class SpeedTestPage : Page
{
    public SpeedTestPage(SpeedTestViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
