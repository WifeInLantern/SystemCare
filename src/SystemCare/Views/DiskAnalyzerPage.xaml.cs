using System.Windows.Controls;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.ViewModels;

namespace SystemCare.Views;

public partial class DiskAnalyzerPage : Page
{
    private readonly DiskAnalyzerViewModel _viewModel;

    public DiskAnalyzerPage(DiskAnalyzerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Treemap.NodeClicked += node => _viewModel.DrillTo(node);
        Treemap.NodeHovered += OnNodeHovered;
    }

    private void OnNodeHovered(FileSystemNode? node) =>
        _viewModel.HoverText = node is null ? "" : $"{node.FullPath} — {ByteFormatter.Format(node.Size)}";
}
