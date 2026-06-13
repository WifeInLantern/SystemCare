using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class EmptyFolderPage : Page
{
    public EmptyFolderPage(EmptyFolderViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
