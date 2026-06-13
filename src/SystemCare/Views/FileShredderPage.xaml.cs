using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class FileShredderPage : Page
{
    public FileShredderPage(FileShredderViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
