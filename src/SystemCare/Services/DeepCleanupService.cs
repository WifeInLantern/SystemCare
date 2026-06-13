using SystemCare.Helpers;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IDeepCleanupService
{
    Task<List<DeepCleanItem>> GetItemsAsync();
    Task RunAsync(IEnumerable<string> itemIds, Action<string> onOutput, CancellationToken ct);
}

/// <summary>
/// Reclaims large amounts of space from Windows-managed areas: the WinSxS component store
/// (via DISM), the previous-Windows folder, Delivery Optimization, the update cache, and
/// upgrade/setup leftovers. All actions need admin (the app is elevated).
/// </summary>
public class DeepCleanupService(IDiskMaintenanceService runner) : IDeepCleanupService
{
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string SystemDrive => Path.GetPathRoot(WinDir) ?? @"C:\";

    private static string WindowsOld => Path.Combine(SystemDrive, "Windows.old");
    private static string DeliveryOpt => Path.Combine(WinDir, "SoftwareDistribution", "DeliveryOptimization");
    private static string UpdateCache => Path.Combine(WinDir, "SoftwareDistribution", "Download");

    private static string[] UpgradeLogPaths =>
    [
        Path.Combine(SystemDrive, "$WINDOWS.~BT"),
        Path.Combine(SystemDrive, "$WINDOWS.~WS"),
        Path.Combine(WinDir, "Panther"),
        Path.Combine(WinDir, "Logs", "CBS"),
    ];

    public Task<List<DeepCleanItem>> GetItemsAsync() => Task.Run(() =>
    {
        var items = new List<DeepCleanItem>
        {
            new()
            {
                Id = "winsxs", Name = "Windows component store (WinSxS)",
                Description = "Remove superseded components with DISM — reclaimable amount varies.",
                SizeBytes = 0, Available = true,
            },
            new()
            {
                Id = "windowsold", Name = "Previous Windows installation (Windows.old)",
                Description = "Files kept after a Windows upgrade. Removing this is permanent.",
                Available = Directory.Exists(WindowsOld),
                SizeBytes = 0, // not measured (folder is huge/slow); shown as present
            },
            new()
            {
                Id = "deliveryopt", Name = "Delivery Optimization files",
                Description = "Cached update files shared with other PCs.",
                SizeBytes = SafeFileEnumerator.Measure(DeliveryOpt).Bytes,
                Available = Directory.Exists(DeliveryOpt),
            },
            new()
            {
                Id = "updatecache", Name = "Windows Update cache",
                Description = "Downloaded update installers Windows no longer needs.",
                SizeBytes = SafeFileEnumerator.Measure(UpdateCache).Bytes,
                Available = Directory.Exists(UpdateCache),
            },
            new()
            {
                Id = "upgradelogs", Name = "Upgrade & setup leftovers",
                Description = "Old upgrade folders ($WINDOWS.~BT/~WS) and setup logs.",
                SizeBytes = UpgradeLogPaths.Where(Directory.Exists).Sum(p => SafeFileEnumerator.Measure(p).Bytes),
                Available = UpgradeLogPaths.Any(Directory.Exists),
            },
        };
        return items.Where(i => i.Available).ToList();
    });

    public async Task RunAsync(IEnumerable<string> itemIds, Action<string> onOutput, CancellationToken ct)
    {
        foreach (var id in itemIds)
        {
            ct.ThrowIfCancellationRequested();
            switch (id)
            {
                case "winsxs":
                    onOutput("=== Cleaning Windows component store (DISM) ===");
                    await runner.RunAsync("DISM", "/Online /Cleanup-Image /StartComponentCleanup", onOutput, null, ct);
                    break;

                case "windowsold":
                    onOutput("=== Removing Windows.old ===");
                    await runner.RunAsync("takeown", $"/f \"{WindowsOld}\" /r /d y", onOutput, null, ct);
                    await runner.RunAsync("icacls", $"\"{WindowsOld}\" /grant *S-1-5-32-544:F /t /c", onOutput, null, ct);
                    await runner.RunAsync("rd", $"/s /q \"{WindowsOld}\"", onOutput, null, ct);
                    break;

                case "deliveryopt":
                    onOutput("=== Clearing Delivery Optimization cache ===");
                    DeleteFolderContents([DeliveryOpt], onOutput, ct);
                    break;

                case "updatecache":
                    onOutput("=== Clearing Windows Update cache ===");
                    DeleteFolderContents([UpdateCache], onOutput, ct);
                    break;

                case "upgradelogs":
                    onOutput("=== Removing upgrade & setup leftovers ===");
                    DeleteFolderContents(UpgradeLogPaths, onOutput, ct);
                    break;
            }
            onOutput("");
        }
        onOutput("=== Deep cleanup finished ===");
    }

    private static void DeleteFolderContents(IEnumerable<string> roots, Action<string> onOutput, CancellationToken ct)
    {
        long bytes = 0;
        int files = 0, skipped = 0;
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in SafeFileEnumerator.EnumerateFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long size = file.Length;
                    File.Delete(file.FullName);
                    bytes += size;
                    files++;
                }
                catch (Exception) { skipped++; }
            }
            // remove now-empty subdirectories (deepest first), keeping the root
            try
            {
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                {
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
        onOutput($"Removed {files:N0} files ({ByteFormatter.Format(bytes)})" + (skipped > 0 ? $", {skipped:N0} in use and skipped." : "."));
    }
}
