using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SystemCare.Helpers;
using SystemCare.Models;
using SystemCare.Services;

namespace SystemCare.ViewModels;

public class HistoryEntryViewModel(HistoryEntry entry)
{
    public string Category => entry.Category;
    public string Summary => entry.Summary;
    public string Icon => entry.Icon;
    public string When => entry.TimestampUtc.ToLocalTime().ToString("g");
    public string Detail =>
        entry.BytesFreed > 0
            ? ByteFormatter.Format(entry.BytesFreed) + (entry.ItemCount > 0 ? $" · {entry.ItemCount} item(s)" : "")
            : entry.ItemCount > 0 ? $"{entry.ItemCount} item(s)" : "";
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _history;

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _isEmpty = true;

    public HistoryViewModel(IHistoryService history) => _history = history;

    public void OnNavigatedTo() => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        var all = _history.GetAll();
        Entries.Clear();
        foreach (var e in all) Entries.Add(new HistoryEntryViewModel(e));

        IsEmpty = all.Count == 0;
        long monthBytes = _history.TotalBytesFreedSince(DateTime.UtcNow.AddDays(-30));
        SummaryText = IsEmpty
            ? "No activity yet — run a scan, cleanup or fix and it will be logged here."
            : $"{all.Count} action(s) recorded · {ByteFormatter.Format(monthBytes)} freed in the last 30 days";
    }

    [RelayCommand]
    private void Clear()
    {
        _history.Clear();
        Refresh();
    }
}
