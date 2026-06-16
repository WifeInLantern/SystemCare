using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Models;
using SystemCare.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace SystemCare.ViewModels;

public partial class DebloatItemViewModel : ObservableObject
{
    public DebloatItem Item { get; }
    public DebloatItemViewModel(DebloatItem item)
    {
        Item = item;
        _isSelected = item.Recommended;
    }

    public string Name => Item.Name;
    public string Description => Item.Description;
    public string Group => Item.Group;
    public bool Reversible => Item.Reversible;
    public bool HasWarning => !string.IsNullOrEmpty(Item.Warning);
    public string Warning => Item.Warning ?? "";
    public string BadgeText => Item.Reversible ? "Reversible" : "Permanent";

    [ObservableProperty] private bool _isSelected;
}

public partial class DebloatViewModel : ObservableObject
{
    private const int MaxOutputChars = 60_000;
    private readonly IDebloatService _service;
    private readonly IContentDialogService _dialogs;
    private readonly IHistoryService _history;
    private readonly StringBuilder _output = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<DebloatItemViewModel> Items { get; } = [];
    /// <summary>Grouped view used by the page (grouped by <see cref="DebloatItem.Group"/>).</summary>
    public ICollectionView GroupedItems { get; }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _createRestorePoint = true;
    [ObservableProperty] private string _outputText =
        "Select what to remove or disable, then Apply. A restore point is created first and most actions can be reverted.";

    public DebloatViewModel(IDebloatService service, IContentDialogService dialogs, IHistoryService history)
    {
        _service = service;
        _dialogs = dialogs;
        _history = history;

        foreach (var item in _service.Items) Items.Add(new DebloatItemViewModel(item));

        GroupedItems = CollectionViewSource.GetDefaultView(Items);
        GroupedItems.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DebloatItemViewModel.Group)));
    }

    private List<string> SelectedIds() => Items.Where(i => i.IsSelected).Select(i => i.Item.Id).ToList();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsRunning) return;
        var selected = SelectedIds();
        if (selected.Count == 0) return;

        bool removesApps = Items.Any(i => i.IsSelected && !i.Item.Reversible);
        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Apply debloat changes?",
            Content = $"{selected.Count} item(s) will be applied." +
                      (CreateRestorePoint ? "\n\nA system restore point will be created first." : "") +
                      (removesApps ? "\n\nWARNING: removing preinstalled apps is PERMANENT — reinstalling means getting them from the Store again." : "\n\nThe selected tweaks can be undone with \"Revert selected\".") ,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        await RunAsync(ct => _service.ApplyAsync(selected, CreateRestorePoint, AppendLine, ct), "Debloat", "Applied");
    }

    [RelayCommand]
    private async Task RevertAsync()
    {
        if (IsRunning) return;
        var selected = Items.Where(i => i.IsSelected && i.Item.Reversible).Select(i => i.Item.Id).ToList();
        if (selected.Count == 0)
        {
            AppendLine("Select one or more reversible items to revert.");
            FlushOutput();
            return;
        }

        var confirm = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Revert selected changes?",
            Content = $"{selected.Count} reversible item(s) will be restored to their Windows defaults.",
            PrimaryButtonText = "Revert",
            CloseButtonText = "Cancel",
        });
        if (confirm != ContentDialogResult.Primary) return;

        await RunAsync(ct => _service.RevertAsync(selected, AppendLine, ct), "Debloat revert", "Reverted");
    }

    private async Task RunAsync(Func<CancellationToken, Task<DebloatResult>> action, string historyCategory, string verb)
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _output.Clear();
        OutputText = "";
        try
        {
            var result = await action(_cts.Token);
            if (result.Applied > 0)
                _history.Record(historyCategory, $"{verb} {result.Applied} item(s)", 0, result.Applied, "Broom24");
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
