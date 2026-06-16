using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class DebloatPage : Page
{
    public DebloatPage(DebloatViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnOutputChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) box.ScrollToEnd();
    }
}
