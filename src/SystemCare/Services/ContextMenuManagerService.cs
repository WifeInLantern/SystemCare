using Microsoft.Win32;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IContextMenuManagerService
{
    Task<List<ContextMenuEntry>> ListAsync(CancellationToken ct);
    /// <summary>
    /// Enables/disables a handler by adding or removing a leading "-" on its CLSID (the documented,
    /// fully reversible way to disable a shell extension without deleting it). Returns (ok, message).
    /// </summary>
    Task<(bool Ok, string Message)> SetEnabledAsync(ContextMenuEntry entry, bool enabled);
}

public class ContextMenuManagerService : IContextMenuManagerService
{
    private readonly ILogService _log;
    public ContextMenuManagerService(ILogService log) => _log = log;

    // Registry roots (under HKEY_CLASSES_ROOT) that hold right-click handlers, with friendly labels.
    private static readonly (string Root, string Location)[] Roots =
    [
        ("*", "All files"),
        ("AllFilesystemObjects", "Files & folders"),
        ("Directory", "Folders"),
        (@"Directory\Background", "Desktop background"),
        ("Folder", "Folder"),
        ("Drive", "Drives"),
    ];

    public Task<List<ContextMenuEntry>> ListAsync(CancellationToken ct) => Task.Run(() =>
    {
        var list = new List<ContextMenuEntry>();
        foreach (var (root, location) in Roots)
        {
            ct.ThrowIfCancellationRequested();
            string handlersPath = $@"{root}\shellex\ContextMenuHandlers";
            try
            {
                using var handlers = Registry.ClassesRoot.OpenSubKey(handlersPath);
                if (handlers is null) continue;

                foreach (var name in handlers.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = handlers.OpenSubKey(name);
                        string? value = sub?.GetValue(null) as string;
                        if (string.IsNullOrWhiteSpace(value)) continue;

                        bool enabled = !value.TrimStart().StartsWith('-');
                        string friendly = string.IsNullOrWhiteSpace(name) || name.StartsWith('{')
                            ? ResolveClsidName(value.TrimStart('-')) ?? name
                            : name;

                        list.Add(new ContextMenuEntry($@"{handlersPath}\{name}", friendly, location, enabled));
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception ex) { _log.Warn("ContextMenu", $"Read failed for {handlersPath}: {ex.Message}"); }
        }
        return list
            .GroupBy(e => e.KeyPath)                 // guard against duplicates
            .Select(g => g.First())
            .OrderBy(e => e.Name)
            .ToList();
    }, ct);

    private static string? ResolveClsidName(string clsid)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
            return key?.GetValue(null) as string;
        }
        catch (Exception) { return null; }
    }

    public Task<(bool Ok, string Message)> SetEnabledAsync(ContextMenuEntry entry, bool enabled) => Task.Run(() =>
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(entry.KeyPath, writable: true);
            if (key is null) return (false, "Registry key not found.");
            if (key.GetValue(null) is not string value) return (false, "Handler has no CLSID value.");

            if (enabled && value.StartsWith('-')) key.SetValue(null, value[1..]);
            else if (!enabled && !value.StartsWith('-')) key.SetValue(null, "-" + value);

            _log.Info("ContextMenu", $"{(enabled ? "Enabled" : "Disabled")} {entry.Name} ({entry.Location}).");
            return (true, $"{entry.Name} {(enabled ? "enabled" : "disabled")}. Restart Explorer or sign out to see the change.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied — administrator rights are required.");
        }
        catch (Exception ex)
        {
            _log.Error("ContextMenu", "Toggle failed", ex);
            return (false, ex.Message);
        }
    });
}
