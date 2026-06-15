using System.Text;

namespace SystemCare.Services;

public enum LogLevel { Info, Warn, Error }

/// <summary>
/// Lightweight, dependency-free diagnostic logger. Writes one file per day under
/// <c>%AppData%\SystemCare\logs</c>, is safe to call from any thread, never throws, and prunes files
/// older than <see cref="RetentionDays"/> days so the folder can't grow without bound.
/// </summary>
public interface ILogService
{
    string LogDirectory { get; }
    void Info(string category, string message);
    void Warn(string category, string message);
    void Error(string category, string message, Exception? ex = null);
}

public class LogService : ILogService
{
    private const int RetentionDays = 14;
    private readonly object _gate = new();

    public string LogDirectory { get; }

    public LogService(ISettingsService settings)
    {
        LogDirectory = Path.Combine(settings.SettingsDirectory, "logs");
        try
        {
            Directory.CreateDirectory(LogDirectory);
            PruneOldLogs();
        }
        catch (Exception) { /* logging must never break the app */ }
    }

    private string CurrentFile => Path.Combine(LogDirectory, $"systemcare-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string category, string message) => Write(LogLevel.Info, category, message, null);
    public void Warn(string category, string message) => Write(LogLevel.Warn, category, message, null);
    public void Error(string category, string message, Exception? ex = null) => Write(LogLevel.Error, category, message, ex);

    private void Write(LogLevel level, string category, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] [")
                .Append(category).Append("] ").Append(message);
            if (ex is not null) sb.Append(" :: ").Append(ex);
            sb.AppendLine();

            lock (_gate)
                File.AppendAllText(CurrentFile, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception) { /* disk full / locked file / etc. — swallow */ }
    }

    private void PruneOldLogs()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var f in Directory.GetFiles(LogDirectory, "systemcare-*.log"))
        {
            try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); }
            catch (Exception) { }
        }
    }
}
