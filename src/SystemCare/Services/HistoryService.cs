using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IHistoryService
{
    /// <summary>Appends an action to the activity log. Safe to call from any thread; never throws.</summary>
    void Record(string category, string summary, long bytesFreed = 0, int itemCount = 0, string icon = "History24");
    /// <summary>Most-recent-first.</summary>
    IReadOnlyList<HistoryEntry> GetAll();
    long TotalBytesFreedSince(DateTime utc);
    void Clear();
}

/// <summary>
/// A small append-only activity log so the app remembers what maintenance it has done
/// (junk cleaned, RAM freed, programs removed, drivers updated…). Persisted as JSON next to
/// settings.json and capped to the most recent <see cref="MaxEntries"/> entries.
/// </summary>
public class HistoryService : IHistoryService
{
    private const int MaxEntries = 500;
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare", "history.json");
    private List<HistoryEntry>? _cache;

    public void Record(string category, string summary, long bytesFreed = 0, int itemCount = 0, string icon = "History24")
    {
        lock (_gate)
        {
            var list = Load();
            list.Insert(0, new HistoryEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Category = category,
                Summary = summary,
                BytesFreed = bytesFreed,
                ItemCount = itemCount,
                Icon = icon,
            });
            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
            Save(list);
        }
    }

    public IReadOnlyList<HistoryEntry> GetAll()
    {
        lock (_gate) return Load().ToList();
    }

    public long TotalBytesFreedSince(DateTime utc)
    {
        lock (_gate) return Load().Where(e => e.TimestampUtc >= utc).Sum(e => e.BytesFreed);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _cache = [];
            Save(_cache);
        }
    }

    private List<HistoryEntry> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception)
        {
            _cache = [];
        }
        return _cache;
    }

    private void Save(List<HistoryEntry> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // history is best-effort; never disrupt an actual maintenance action over a failed log write
        }
    }
}
