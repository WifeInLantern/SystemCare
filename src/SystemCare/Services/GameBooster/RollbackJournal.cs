using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services.GameBooster;

/// <summary>A persisted Game Booster session: the ordered records applied, and whether it closed cleanly.</summary>
public sealed class JournalSession
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public bool Open { get; set; } = true;
    public List<OptimizationRecord> Records { get; set; } = [];
}

public interface IRollbackJournal
{
    /// <summary>Starts a fresh session file (open = true).</summary>
    void Begin();
    /// <summary>Appends an applied optimization's record and flushes to disk.</summary>
    void Append(OptimizationRecord record);
    /// <summary>Reads the current session, or null if none exists / unreadable.</summary>
    JournalSession? Read();
    /// <summary>Deletes the session file (called after a clean revert).</summary>
    void Clear();
}

/// <summary>
/// Durable journal at %AppData%\SystemCare\gamebooster\session.json. Writing each record as it applies means an
/// app crash or power loss mid-session leaves a replayable record set, so the system can be restored on next launch.
/// </summary>
public sealed class RollbackJournal : IRollbackJournal
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly ILogService _log;
    private readonly string _path;
    private JournalSession _current = new();

    public RollbackJournal(ILogService log)
    {
        _log = log;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SystemCare", "gamebooster");
        _path = Path.Combine(dir, "session.json");
    }

    public void Begin()
    {
        _current = new JournalSession();
        Save();
    }

    public void Append(OptimizationRecord record)
    {
        _current.Records.Add(record);
        Save();
    }

    public JournalSession? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<JournalSession>(File.ReadAllText(_path));
        }
        catch (Exception ex)
        {
            _log.Warn("GameBooster", $"Could not read rollback journal: {ex.Message}");
            return null;
        }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (Exception ex) { _log.Warn("GameBooster", $"Could not clear rollback journal: {ex.Message}"); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_current, Json));
        }
        catch (Exception ex)
        {
            _log.Warn("GameBooster", $"Could not write rollback journal: {ex.Message}");
        }
    }
}
