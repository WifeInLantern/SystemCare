using SystemCare.Models;

namespace SystemCare.Services;

public interface ISoftwareUpdateService
{
    /// <summary>True if winget (App Installer) is present and runnable.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    /// <summary>Lists installed apps with a newer version available (<c>winget upgrade</c>). Never throws.</summary>
    Task<List<SoftwareUpdate>> GetUpgradesAsync(CancellationToken ct);
    /// <summary>Applies the selected upgrades one by one, reporting per-app progress.</summary>
    Task<SoftwareUpdateResult> UpgradeAsync(IEnumerable<SoftwareUpdate> picks, IProgress<SoftwareUpdateProgress>? progress, CancellationToken ct);
}

/// <summary>
/// Updates installed Win32/Store apps via the Windows Package Manager (winget). Process launching and winget
/// discovery live in <see cref="IWingetRunner"/>; the upgrade list is parsed by <see cref="WingetUpgradeParser"/>.
/// </summary>
public class SoftwareUpdateService(ILogService log, IWingetRunner winget) : ISoftwareUpdateService
{
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!winget.IsInstalled) return false;
        var (exit, output) = await winget.RunAsync("--version", ct);
        return exit == 0 && output.TrimStart().StartsWith('v');
    }

    public async Task<List<SoftwareUpdate>> GetUpgradesAsync(CancellationToken ct)
    {
        if (!winget.IsInstalled) return [];
        var (_, output) = await winget.RunAsync(
            "upgrade --include-unknown --disable-interactivity --accept-source-agreements", ct);
        return WingetUpgradeParser.Parse(output, log);
    }

    public async Task<SoftwareUpdateResult> UpgradeAsync(
        IEnumerable<SoftwareUpdate> picks, IProgress<SoftwareUpdateProgress>? progress, CancellationToken ct)
    {
        var list = picks.ToList();
        if (list.Count == 0) return new SoftwareUpdateResult { Message = "No apps selected." };

        int updated = 0, failed = 0, i = 0;
        foreach (var app in list)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            progress?.Report(new SoftwareUpdateProgress
            {
                Current = i, Total = list.Count, Name = app.Name, Percent = (i - 1) * 100.0 / list.Count,
            });

            var (exit, _) = await winget.RunAsync(
                $"upgrade --id \"{app.Id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                ct);
            if (exit == 0)
            {
                updated++;
                log.Info("SoftwareUpdate", $"Updated {app.Id} → {app.AvailableVersion}");
            }
            else
            {
                failed++;
                log.Warn("SoftwareUpdate", $"winget exited {exit} updating {app.Id} ({app.Name})");
            }

            progress?.Report(new SoftwareUpdateProgress
            {
                Current = i, Total = list.Count, Name = app.Name, Percent = i * 100.0 / list.Count,
            });
        }

        string msg = $"Updated {updated} app(s)." + (failed > 0 ? $" {failed} could not be updated." : "");
        return new SoftwareUpdateResult { Updated = updated, Failed = failed, Message = msg };
    }
}
