using System.Windows;
using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class BreachCheckPage : Page
{
    private readonly BreachCheckViewModel _viewModel;

    public BreachCheckPage(BreachCheckViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    // PasswordBox.Password isn't a bindable DependencyProperty (by design), so the check is driven
    // from code-behind. The password is passed straight to the VM and never stored.
    private async void OnCheckClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CheckAsync(PasswordInput.Password);
    }
}
