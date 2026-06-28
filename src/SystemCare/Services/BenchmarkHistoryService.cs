using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IBenchmarkHistoryService
{
    void Add(BenchmarkRun run);
    /// <summary>Oldest-first, for the score trend chart.</summary>
    IReadOnlyList<BenchmarkRun> GetAll();
}

/// <summary>
/// Persists the last few benchmark runs as JSON next to settings.json so the page can draw a score trend.
/// Same shape as <see cref="HistoryService"/>: lock-guarded, best-effort, capped, never throws.
/// </summary>
public sealed class BenchmarkHistoryService : IBenchmarkHistoryService
{
    private const int MaxRuns = 30;
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare", "benchmark-history.json");
    private List<BenchmarkRun>? _cache;

    public void Add(BenchmarkRun run)
    {
        lock (_gate)
        {
            var list = Load();
            list.Add(run);
            if (list.Count > MaxRuns) list.RemoveRange(0, list.Count - MaxRuns);
            Save(list);
        }
    }

    public IReadOnlyList<BenchmarkRun> GetAll()
    {
        lock (_gate) return Load().ToList();
    }

    private List<BenchmarkRun> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<BenchmarkRun>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception)
        {
            _cache = [];
        }
        return _cache;
    }

    private void Save(List<BenchmarkRun> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // trend history is best-effort; never disrupt a run over a failed write
        }
    }
}
