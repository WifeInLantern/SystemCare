using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class DeepCleanItemViewModel(DeepCleanItem item) : ObservableObject
{
    public DeepCleanItem Item { get; } = item;
    public string Name => Item.Name;
    public string Description => Item.Description;
    public string SizeText => Item.SizeBytes > 0 ? ByteFormatter.Format(Item.SizeBytes) : "—";
    [ObservableProperty] private bool _isSelected;
}

public partial class DeepCleanupViewModel : ObservableObject
{
    private const int MaxOutputChars = 60_000;
    private readonly IDeepCleanupService _service;
    private readonly IContentDialogService _dialogs;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<DeepCleanItemViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _outputText = "Select items and run to reclaim space. Output appears here.";

    public DeepCleanupViewModel(IDeepCleanupService service, IContentDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
    }

    public async void OnNavigatedTo()
    {
        if (Items.Count > 0) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var items = await _service.GetItemsAsync();
            Items.Clear();
            foreach (var i in items) Items.Add(new DeepCleanItemViewModel(i));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task CleanAsync()
    {
        if (IsRunning) return;
        var selected = Items.Where(i => i.IsSelected).Select(i => i.Item.Id).ToList();
        if (selected.Count == 0) return;

        bool removingWindowsOld = selected.Contains("windowsold");
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Run deep cleanup?",
            Content = $"{selected.Count} item(s) will be cleaned." +
                      (removingWindowsOld ? "\n\nRemoving the previous Windows installation (Windows.old) is PERMANENT and cannot be undone." : "") +
                      "\n\nThis can take several minutes.",
            PrimaryButtonText = "Clean now",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        _output.Clear();
        OutputText = "";
        try
        {
            await _service.RunAsync(selected, AppendLine, _cts.Token);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            AppendLine("=== Cancelled ===");
        }
        catch (Exception ex)
        {
            AppendLine($"Error: {ex.Message}");
        }
        finally
        {
            FlushOutput();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void AppendLine(string line)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _output.AppendLine(line);
            if (_output.Length > MaxOutputChars) _output.Remove(0, _output.Length - MaxOutputChars);
            OutputText = _output.ToString();
        });
    }

    private void FlushOutput() => Application.Current?.Dispatcher.Invoke(() => OutputText = _output.ToString());
}
