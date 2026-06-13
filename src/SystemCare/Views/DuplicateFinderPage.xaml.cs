using System.Windows.Controls;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class DuplicateFinderPage : Page
{
    public DuplicateFinderPage(DuplicateFinderViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
