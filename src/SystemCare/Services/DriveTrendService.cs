using System.Text.Json;
using SystemCare.Helpers;

namespace SystemCare.Services;

public sealed class DriveSpaceSnapshot
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Drive { get; set; } = "";
    public long FreeBytes { get; set; }
    public long TotalBytes { get; set; }
}

public interface IDriveTrendService
{
    /// <summary>Records one free-space sample per drive per local calendar day (later samples replace earlier ones).</summary>
    void Record(string drive, long freeBytes, long totalBytes);

    /// <summary>Storage Forecast (2.14): "~5 weeks until full at the current rate", or null when
    /// there is no meaningful downward trend (or not enough history yet).</summary>
    string? GetForecastText(string drive);
}

/// <summary>
/// Persists per-drive free-space history as JSON next to settings.json, same shape and discipline
/// as <see cref="HealthTrendService"/>: lock-guarded, best-effort, capped, never throws. The
/// forecast math lives in <see cref="StorageForecast"/> (pure, unit-tested).
/// </summary>
public sealed class DriveTrendService : IDriveTrendService
{
    private const int MaxSnapshots = 1500; // ~1 year of daily samples across a handful of drives
    private const int ForecastWindowDays = 45;

    private readonly object _gate = new();
    private readonly string _path;
    private List<DriveSpaceSnapshot>? _cache;

    public DriveTrendService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemCare", "drive-trend.json"))
    {
    }

    /// <summary>Test seam: redirect persistence to a temp path.</summary>
    internal DriveTrendService(string path) => _path = path;

    public void Record(string drive, long freeBytes, long totalBytes)
    {
        if (string.IsNullOrWhiteSpace(drive) || totalBytes <= 0) return;
        lock (_gate)
        {
            var list = Load();
            var today = DateTime.Now.Date;
            list.RemoveAll(s =>
                string.Equals(s.Drive, drive, StringComparison.OrdinalIgnoreCase) &&
                s.TimestampUtc.ToLocalTime().Date == today);
            list.Add(new DriveSpaceSnapshot { Drive = drive, FreeBytes = freeBytes, TotalBytes = totalBytes });
            if (list.Count > MaxSnapshots) list.RemoveRange(0, list.Count - MaxSnapshots);
            Save(list);
        }
    }

    public string? GetForecastText(string drive)
    {
        List<(DateTime, long)> samples;
        lock (_gate)
        {
            var cutoff = DateTime.UtcNow.AddDays(-ForecastWindowDays);
            samples = Load()
                .Where(s => string.Equals(s.Drive, drive, StringComparison.OrdinalIgnoreCase) && s.TimestampUtc >= cutoff)
                .OrderBy(s => s.TimestampUtc)
                .Select(s => (s.TimestampUtc, s.FreeBytes))
                .ToList();
        }
        return StorageForecast.Describe(StorageForecast.DaysUntilFull(samples));
    }

    private List<DriveSpaceSnapshot> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<DriveSpaceSnapshot>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception)
        {
            _cache = [];
        }
        return _cache;
    }

    private void Save(List<DriveSpaceSnapshot> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(list));
        }
        catch (Exception)
        {
            // trend history is best-effort; never disrupt a refresh over a failed write
        }
    }
}
