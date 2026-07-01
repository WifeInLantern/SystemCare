using SystemCare.Models;

namespace SystemCare.Services;

public interface ISoftwareHubService
{
    /// <summary>True if winget (App Installer) is present and runnable.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    /// <summary>Returns the full static catalog, each entry annotated with whether it's already
    /// installed (via one <c>winget list</c> call). Never throws.</summary>
    Task<List<SoftwareHubAppStatus>> GetCatalogAsync(CancellationToken ct);
    /// <summary>Installs the selected apps one by one, reporting per-app progress.</summary>
    Task<SoftwareHubInstallResult> InstallAsync(IEnumerable<SoftwareHubApp> picks, IProgress<SoftwareHubInstallProgress>? progress, CancellationToken ct);
}

/// <summary>
/// Installs apps from the curated <see cref="SoftwareHubCatalog"/> via the Windows Package Manager
/// (winget). Process launching and winget discovery live in <see cref="IWingetRunner"/>; installed-app
/// detection is parsed by <see cref="WingetListParser"/>.
/// </summary>
public class SoftwareHubService(ILogService log, IWingetRunner winget) : ISoftwareHubService
{
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!winget.IsInstalled) return false;
        var (exit, output) = await winget.RunAsync("--version", ct);
        return exit == 0 && output.TrimStart().StartsWith('v');
    }

    public async Task<List<SoftwareHubAppStatus>> GetCatalogAsync(CancellationToken ct)
    {
        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (winget.IsInstalled)
        {
            var (_, output) = await winget.RunAsync("list --accept-source-agreements --disable-interactivity", ct);
            installedIds = WingetListParser.ParseInstalledIds(output, log);
        }

        return SoftwareHubCatalog.All
            .Select(app => new SoftwareHubAppStatus { App = app, IsInstalled = installedIds.Contains(app.Id) })
            .ToList();
    }

    public async Task<SoftwareHubInstallResult> InstallAsync(
        IEnumerable<SoftwareHubApp> picks, IProgress<SoftwareHubInstallProgress>? progress, CancellationToken ct)
    {
        var list = picks.ToList();
        if (list.Count == 0) return new SoftwareHubInstallResult { Message = "No apps selected." };

        int installed = 0, failed = 0, i = 0;
        foreach (var app in list)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            progress?.Report(new SoftwareHubInstallProgress
            {
                Current = i, Total = list.Count, Name = app.Name, Percent = (i - 1) * 100.0 / list.Count,
            });

            var (exit, _) = await winget.RunAsync(
                $"install --id \"{app.Id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                ct);
            if (exit == 0)
            {
                installed++;
                log.Info("SoftwareHub", $"Installed {app.Id}");
            }
            else
            {
                failed++;
                log.Warn("SoftwareHub", $"winget exited {exit} installing {app.Id} ({app.Name})");
            }

            progress?.Report(new SoftwareHubInstallProgress
            {
                Current = i, Total = list.Count, Name = app.Name, Percent = i * 100.0 / list.Count,
            });
        }

        string msg = $"Installed {installed} app(s)." + (failed > 0 ? $" {failed} could not be installed." : "");
        return new SoftwareHubInstallResult { Installed = installed, Failed = failed, Message = msg };
    }
}
