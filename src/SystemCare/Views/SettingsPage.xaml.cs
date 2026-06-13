using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
