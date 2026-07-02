using SystemCare.Models;

namespace SystemCare.Services;

public interface ISoftwareHubService
{
    /// <summary>True if winget (App Installer) is present and runnable.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    /// <summary>Returns the full static catalog, each entry annotated with whether it's already
    /// installed (via one <c>winget list</c> call). Never throws.</summary>
    Task<List<SoftwareHubAppStatus>> GetCatalogAsync(CancellationToken ct);
    /// <summary>Searches the winget catalog for <paramref name="query"/>, annotating results with
    /// whether they're already installed.</summary>
    Task<List<SoftwareHubAppStatus>> SearchAsync(string query, CancellationToken ct);
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
    // Installed-ID set cached across catalog loads and searches (one `winget list` costs seconds);
    // invalidated after every install so freshly-installed apps flip to the Installed badge.
    private HashSet<string>? _installedCache;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!winget.IsInstalled) return false;
        var (exit, output) = await winget.RunAsync("--version", ct);
        return exit == 0 && output.TrimStart().StartsWith('v');
    }

    public async Task<List<SoftwareHubAppStatus>> GetCatalogAsync(CancellationToken ct)
    {
        _installedCache = null; // an explicit refresh should re-check what's installed
        var installedIds = await GetInstalledIdsAsync(ct);

        return SoftwareHubCatalog.All
            .Select(app => new SoftwareHubAppStatus { App = app, IsInstalled = installedIds.Contains(app.Id) })
            .ToList();
    }

    public async Task<List<SoftwareHubAppStatus>> SearchAsync(string query, CancellationToken ct)
    {
        // The argument string is raw — strip quotes rather than trying to escape them.
        string q = query.Replace("\"", "").Trim();
        if (q.Length == 0) return [];

        // --source winget: msstore results have store-licence install semantics and non-DNS IDs;
        // restricting the source keeps every row installable with the same `install --id --exact` path.
        var (_, output) = await winget.RunAsync(
            $"search \"{q}\" --source winget --accept-source-agreements --disable-interactivity", ct);
        var results = WingetSearchParser.Parse(output, log);
        var installedIds = await GetInstalledIdsAsync(ct);

        return results.Select(r => new SoftwareHubAppStatus
        {
            App = new SoftwareHubApp
            {
                Name = r.Name,
                Id = r.Id,
                Category = "Search results",
                Description = string.Join(" · ", new[] { r.Version, r.Source }.Where(s => s.Length > 0)),
            },
            IsInstalled = installedIds.Contains(r.Id),
        }).ToList();
    }

    private async Task<HashSet<string>> GetInstalledIdsAsync(CancellationToken ct)
    {
        if (_installedCache is not null) return _installedCache;
        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (winget.IsInstalled)
        {
            var (_, output) = await winget.RunAsync("list --accept-source-agreements --disable-interactivity", ct);
            installedIds = WingetListParser.ParseInstalledIds(output, log);
        }
        return _installedCache = installedIds;
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

        if (installed > 0) _installedCache = null;

        string msg = $"Installed {installed} app(s)." + (failed > 0 ? $" {failed} could not be installed." : "");
        return new SoftwareHubInstallResult { Installed = installed, Failed = failed, Message = msg };
    }
}
