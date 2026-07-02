using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IHealthTrendService
{
    /// <summary>Records a health score for today (replacing any earlier sample from the same
    /// local calendar day, so repeated scans don't flood the trend).</summary>
    void Record(int score);
    /// <summary>Oldest-first, for the trend chart.</summary>
    IReadOnlyList<HealthSnapshot> GetAll();
}

/// <summary>
/// Persists one health-score snapshot per day as JSON next to settings.json so the Care Report can
/// draw a long-term trend. Same shape as <see cref="BenchmarkHistoryService"/>: lock-guarded,
/// best-effort, capped, never throws.
/// </summary>
public sealed class HealthTrendService : IHealthTrendService
{
    private const int MaxSnapshots = 365;
    private readonly object _gate = new();
    private readonly string _path;
    private List<HealthSnapshot>? _cache;

    public HealthTrendService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare", "health-trend.json"))
    {
    }

    /// <summary>Test seam: redirect persistence to a temp path.</summary>
    internal HealthTrendService(string path) => _path = path;

    public void Record(int score)
    {
        lock (_gate)
        {
            var list = Load();
            var today = DateTime.Now.Date;
            list.RemoveAll(s => s.TimestampUtc.ToLocalTime().Date == today);
            list.Add(new HealthSnapshot { TimestampUtc = DateTime.UtcNow, Score = score });
            if (list.Count > MaxSnapshots) list.RemoveRange(0, list.Count - MaxSnapshots);
            Save(list);
        }
    }

    public IReadOnlyList<HealthSnapshot> GetAll()
    {
        lock (_gate) return Load().ToList();
    }

    private List<HealthSnapshot> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<HealthSnapshot>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception)
        {
            _cache = [];
        }
        return _cache;
    }

    private void Save(List<HealthSnapshot> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // trend history is best-effort; never disrupt a scan over a failed write
        }
    }
}
