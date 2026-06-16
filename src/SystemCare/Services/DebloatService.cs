using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using SystemCare.Models;
using Task = System.Threading.Tasks.Task;

namespace SystemCare.Services;

public interface IDebloatService
{
    IReadOnlyList<DebloatItem> Items { get; }
    Task<DebloatResult> ApplyAsync(IEnumerable<string> ids, bool createRestorePoint, Action<string> onOutput, CancellationToken ct);
    Task<DebloatResult> RevertAsync(IEnumerable<string> ids, Action<string> onOutput, CancellationToken ct);
}

/// <summary>
/// Curated Windows debloater. Acts ONLY on the hardcoded allowlist below — specific telemetry services,
/// registry policy values, telemetry scheduled tasks, optional services, and named bloat apps — so it can
/// never touch a critical component the user might type in. Tweaks are reversible (services are disabled,
/// not deleted; registry values are restored); app removal is permanent and flagged as such. Every action
/// is idempotent, wrapped in try/catch, streamed to the on-screen log, and recorded via <see cref="ILogService"/>.
/// All operations require elevation (the app runs as administrator).
/// </summary>
public class DebloatService : IDebloatService
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    // Curated bloat AppX name fragments (matched against package/DisplayName). Constants only — no user input.
    private static readonly string[] BloatApps =
    [
        "BingNews", "BingWeather", "BingFinance", "BingSports", "ZuneMusic", "ZuneVideo",
        "Microsoft.People", "windowscommunicationsapps", "SolitaireCollection", "Clipchamp", "Todos",
        "GetHelp", "Getstarted", "MixedReality", "FeedbackHub", "WindowsMaps", "3DBuilder", "Print3D",
        "MicrosoftOfficeHub", "Microsoft.Messaging", "OneConnect", "SkypeApp", "YourPhone",
    ];

    private sealed record Handler(DebloatItem Item, Func<Action<string>, bool> Apply, Func<Action<string>, bool>? Revert);

    private readonly IRestorePointService _restore;
    private readonly ILogService _log;
    private readonly List<Handler> _handlers;

    public IReadOnlyList<DebloatItem> Items { get; }

    public DebloatService(IRestorePointService restore, ILogService log)
    {
        _restore = restore;
        _log = log;
        _handlers = BuildHandlers();
        Items = _handlers.Select(h => h.Item).ToList();
    }

    // ---------------- apply / revert orchestration ----------------

    public async Task<DebloatResult> ApplyAsync(IEnumerable<string> ids, bool createRestorePoint,
        Action<string> onOutput, CancellationToken ct)
    {
        var wanted = ids.ToHashSet();
        var handlers = _handlers.Where(h => wanted.Contains(h.Item.Id)).ToList();
        var result = new DebloatResult();
        if (handlers.Count == 0) { result.Message = "Nothing selected."; return result; }

        if (createRestorePoint)
        {
            onOutput("Creating a restore point…");
            try
            {
                var (ok, message) = await _restore.CreateRestorePointAsync("Before SystemCare debloat");
                result.RestorePointMade = ok;
                onOutput(ok ? "Restore point created." : $"Restore point not created: {message}");
            }
            catch (Exception ex)
            {
                onOutput($"Restore point failed: {ex.Message}");
            }
        }

        await Task.Run(() => Run(handlers, apply: true, result, onOutput, ct), ct);
        return result;
    }

    public Task<DebloatResult> RevertAsync(IEnumerable<string> ids, Action<string> onOutput, CancellationToken ct)
    {
        var wanted = ids.ToHashSet();
        var handlers = _handlers.Where(h => wanted.Contains(h.Item.Id) && h.Revert is not null).ToList();
        var result = new DebloatResult();
        return Task.Run(() => { Run(handlers, apply: false, result, onOutput, ct); return result; }, ct);
    }

    private void Run(List<Handler> handlers, bool apply, DebloatResult result, Action<string> onOutput, CancellationToken ct)
    {
        foreach (var h in handlers)
        {
            ct.ThrowIfCancellationRequested();
            onOutput($"=== {(apply ? "" : "Revert: ")}{h.Item.Name} ===");
            var op = apply ? h.Apply : h.Revert;
            if (op is null) { onOutput("  (no revert for this item)"); continue; }
            try
            {
                bool changed = op(onOutput);
                if (changed) result.Applied++; else result.Skipped++;
                _log.Info("Debloat", $"{(apply ? "Apply" : "Revert")} {h.Item.Id}: changed={changed}");
            }
            catch (Exception ex)
            {
                result.Failed++;
                onOutput($"  Failed: {ex.Message}");
                _log.Error("Debloat", $"{(apply ? "Apply" : "Revert")} {h.Item.Id} failed", ex);
            }
        }
        result.Message = $"{result.Applied} applied, {result.Skipped} already set"
            + (result.Failed > 0 ? $", {result.Failed} failed" : "") + ".";
        onOutput($"=== Done — {result.Message} ===");
    }

    // ---------------- the curated allowlist ----------------

    private List<Handler> BuildHandlers()
    {
        const string telem = "Telemetry & data collection";
        const string ads = "Ads & suggestions";
        const string search = "Search & Cortana";
        const string svc = "Optional services";
        const string apps = "Preinstalled apps";

        return
        [
            new Handler(
                new DebloatItem { Id = "telemetry", Group = telem, Recommended = true,
                    Name = "Disable telemetry & data collection",
                    Description = "Stops the Connected User Experiences and Telemetry (DiagTrack) and WAP push services and sets diagnostic data to the minimum." },
                log =>
                {
                    bool c = SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, log);
                    c |= DisableService("DiagTrack", log);
                    c |= DisableService("dmwappushservice", log);
                    return c;
                },
                log =>
                {
                    bool c = DeleteValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", log);
                    c |= EnableService("DiagTrack", 2, start: true, log);
                    c |= EnableService("dmwappushservice", 3, start: false, log);
                    return c;
                }),

            new Handler(
                new DebloatItem { Id = "telemetry-tasks", Group = telem, Recommended = true,
                    Name = "Disable telemetry scheduled tasks",
                    Description = "Turns off the Compatibility Appraiser, Customer Experience Improvement, and feedback tasks that collect and upload usage data." },
                log => ForEachTask(false, log),
                log => ForEachTask(true, log)),

            new Handler(
                new DebloatItem { Id = "advertising-id", Group = ads, Recommended = true,
                    Name = "Turn off the advertising ID",
                    Description = "Stops apps from using a per-user advertising identifier to tailor ads." },
                log => SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, log),
                log => SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1, log)),

            new Handler(
                new DebloatItem { Id = "suggestions", Group = ads, Recommended = true,
                    Name = "Disable suggestions & tips",
                    Description = "Turns off Start menu suggestions, 'suggested' apps, tips, and lock-screen promotions." },
                log => SetSuggestions(0, log),
                log => SetSuggestions(1, log)),

            new Handler(
                new DebloatItem { Id = "consumer-features", Group = ads, Recommended = true,
                    Name = "Stop auto-installed bloat (consumer features)",
                    Description = "Sets DisableWindowsConsumerFeatures so Windows stops silently installing promoted/sponsored apps." },
                log => SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, log),
                log => DeleteValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", log)),

            new Handler(
                new DebloatItem { Id = "cortana", Group = search, Recommended = true,
                    Name = "Disable Cortana",
                    Description = "Turns off Cortana via policy." },
                log => SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, log),
                log => DeleteValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", log)),

            new Handler(
                new DebloatItem { Id = "web-search", Group = search, Recommended = true,
                    Name = "Disable Bing / web search in Start",
                    Description = "Keeps Start menu search local instead of sending queries to Bing." },
                log =>
                {
                    bool c = SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, log);
                    c |= SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1, log);
                    return c;
                },
                log =>
                {
                    bool c = SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 1, log);
                    c |= DeleteValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", log);
                    return c;
                }),

            new Handler(
                new DebloatItem { Id = "svc-xbox", Group = svc, Recommended = false, Warning = "Breaks the Xbox app, Game Bar and cloud saves.",
                    Name = "Disable Xbox services",
                    Description = "Disables XblAuthManager, XblGameSave, XboxGipSvc and XboxNetApiSvc." },
                log => DisableServices(log, "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc"),
                log => EnableServices(log, 3, ("XblAuthManager", false), ("XblGameSave", false), ("XboxGipSvc", false), ("XboxNetApiSvc", false))),

            new Handler(
                new DebloatItem { Id = "svc-maps", Group = svc, Recommended = false, Warning = "Offline maps stop updating.",
                    Name = "Disable Maps broker",
                    Description = "Disables the Downloaded Maps Manager (MapsBroker)." },
                log => DisableService("MapsBroker", log),
                log => EnableService("MapsBroker", 2, start: false, log)),

            new Handler(
                new DebloatItem { Id = "svc-fax", Group = svc, Recommended = false, Warning = "Disables Windows Fax.",
                    Name = "Disable Fax service",
                    Description = "Disables the Fax service." },
                log => DisableService("Fax", log),
                log => EnableService("Fax", 3, start: false, log)),

            new Handler(
                new DebloatItem { Id = "svc-wmp", Group = svc, Recommended = false, Warning = "Turns off Windows Media Player network sharing.",
                    Name = "Disable WMP network sharing",
                    Description = "Disables the Windows Media Player Network Sharing Service (WMPNetworkSvc)." },
                log => DisableService("WMPNetworkSvc", log),
                log => EnableService("WMPNetworkSvc", 3, start: false, log)),

            new Handler(
                new DebloatItem { Id = "remove-apps", Group = apps, Recommended = false, Reversible = false,
                    Warning = "Permanent — reinstalling means getting each app from the Store again.",
                    Name = "Remove common preinstalled apps",
                    Description = "Removes built-in bloat (Bing News/Weather, Zune Music/Video, Solitaire, Clipchamp, Get Help, Mixed Reality Portal, Feedback Hub, 3D Builder, Office Hub, Maps, and similar) for all users and deprovisions them so they don't return on new accounts." },
                log => RemoveBloatApps(log),
                null), // permanent — no revert
        ];
    }

    // ---------------- service helpers (disable = Start 4, not deletion) ----------------

    private bool DisableServices(Action<string> log, params string[] names)
    {
        bool changed = false;
        foreach (var n in names) changed |= DisableService(n, log);
        return changed;
    }

    private bool EnableServices(Action<string> log, int defaultStart, params (string Name, bool Start)[] services)
    {
        bool changed = false;
        foreach (var s in services) changed |= EnableService(s.Name, defaultStart, s.Start, log);
        return changed;
    }

    private bool DisableService(string name, Action<string> log)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesKey}\{name}", writable: true);
        if (key is null) { log($"  {name}: not present — skipped"); return false; }

        bool changed = (key.GetValue("Start") as int?) != 4;
        key.SetValue("Start", 4, RegistryValueKind.DWord); // 4 = Disabled (reversible)
        StopService(name, log);
        log(changed ? $"  {name}: disabled" : $"  {name}: already disabled");
        return changed;
    }

    private bool EnableService(string name, int defaultStart, bool start, Action<string> log)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesKey}\{name}", writable: true);
        if (key is null) { log($"  {name}: not present — skipped"); return false; }

        bool changed = (key.GetValue("Start") as int?) != defaultStart;
        key.SetValue("Start", defaultStart, RegistryValueKind.DWord);
        if (start) StartService(name, log);
        log($"  {name}: re-enabled (Start={defaultStart})");
        return changed;
    }

    private static void StopService(string name, Action<string> log)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
            }
        }
        catch (Exception) { /* not installed / can't stop right now — the Start=4 still applies on reboot */ }
    }

    private static void StartService(string name, Action<string> log)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
            }
        }
        catch (Exception) { }
    }

    // ---------------- registry helpers ----------------

    private static bool SetDword(RegistryKey root, string path, string name, int value, Action<string> log)
    {
        using var key = root.CreateSubKey(path);
        if (key is null) { log($"  {name}: key unavailable — skipped"); return false; }
        bool changed = (key.GetValue(name) as int?) != value;
        key.SetValue(name, value, RegistryValueKind.DWord);
        log(changed ? $"  {name} = {value}" : $"  {name}: already {value}");
        return changed;
    }

    private static bool DeleteValue(RegistryKey root, string path, string name, Action<string> log)
    {
        using var key = root.OpenSubKey(path, writable: true);
        if (key?.GetValue(name) is null) { log($"  {name}: already at default"); return false; }
        key.DeleteValue(name, throwOnMissingValue: false);
        log($"  {name}: restored to default");
        return true;
    }

    // ---------------- scheduled tasks ----------------

    private static readonly string[] TelemetryTasks =
    [
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload",
    ];

    private static bool ForEachTask(bool enabled, Action<string> log)
    {
        bool changed = false;
        try
        {
            using var ts = new TaskService();
            foreach (var path in TelemetryTasks)
            {
                try
                {
                    var task = ts.GetTask(path);
                    if (task is null) { log($"  task not present: {Path.GetFileName(path)}"); continue; }
                    if (task.Enabled != enabled) { task.Enabled = enabled; changed = true; }
                    log($"  {Path.GetFileName(path)}: {(enabled ? "enabled" : "disabled")}");
                }
                catch (Exception ex) { log($"  task error ({Path.GetFileName(path)}): {ex.Message}"); }
            }
        }
        catch (Exception ex) { log($"  Task Scheduler unavailable: {ex.Message}"); }
        return changed;
    }

    // ---------------- suggestions (ContentDeliveryManager) ----------------

    private static readonly string[] SuggestionValues =
    [
        "SystemPaneSuggestionsEnabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled",
        "SubscribedContent-353698Enabled", "SilentInstalledAppsEnabled", "SoftLandingEnabled",
    ];

    private static bool SetSuggestions(int value, Action<string> log)
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        bool changed = false;
        foreach (var name in SuggestionValues) changed |= SetDword(Registry.CurrentUser, path, name, value, log);
        return changed;
    }

    // ---------------- app removal (permanent) ----------------

    private bool RemoveBloatApps(Action<string> log)
    {
        log($"  Removing {BloatApps.Length} built-in app group(s) (per-user + provisioned)…");
        // Constant names only — no user input is interpolated, so the script can't be injected.
        string names = string.Join(",", BloatApps.Select(n => "'" + n + "'"));
        string script =
            $"$names=@({names}); foreach($n in $names){{ " +
            "Get-AppxPackage -AllUsers \"*$n*\" | Remove-AppxPackage -ErrorAction SilentlyContinue; " +
            "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like \"*$n*\" } | " +
            "Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue }";

        int exit = RunPowerShell(script);
        log(exit == 0 ? "  Removal completed." : $"  Removal finished (exit {exit}); some apps may have been protected or already gone.");
        return true;
    }

    private static int RunPowerShell(string command)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(180_000);
            return p.ExitCode;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
