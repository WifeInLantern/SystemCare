using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IAppPackageService
{
    Task<List<AppPackage>> GetPackagesAsync();
    Task<(bool Ok, string Message)> UninstallAsync(AppPackage package);

    /// <summary>
    /// Removes several packages in a single PowerShell process (one spawn instead of one per app),
    /// reporting each app's display name via <paramref name="onProgress"/> as it starts.
    /// </summary>
    Task<List<(AppPackage Package, bool Ok)>> UninstallManyAsync(
        IReadOnlyList<AppPackage> packages, Action<AppPackage>? onProgress = null);
}

/// <summary>
/// Lists and removes Microsoft Store / UWP (AppX) packages via PowerShell. Frameworks and
/// OS-critical "System" packages are filtered out so the user can only remove safe, user-facing apps.
/// </summary>
public class AppPackageService : IAppPackageService
{
    // OS-critical packages that must never be offered for removal.
    private static readonly string[] Protected =
    [
        "WindowsStore", "StorePurchaseApp", "DesktopAppInstaller", "SecHealthUI",
        "ShellExperienceHost", "StartMenuExperienceHost", "Windows.Cortana", "AAD.BrokerPlugin",
        "AccountsControl", "LockApp", "ContentDeliveryManager", "UI.Xaml", "VCLibs", "NET.Native",
        "Services.Store", "Client.WebExperience", "Client.CBS", "Client.Core", "CapturePicker",
        "XGpuEjectDialog", "AsyncTextService", "CallingShellApp", "ParentalControls", "PeopleExperienceHost",
        "PinningConfirmationDialog", "Win32WebViewHost", "Apprep.ChxApp", "AssignedAccessLockApp",
        "CredDialogHost", "ECApp", "Hexpansion", "ImmersiveControlPanel", "OOBENetworkConnectionFlow",
        "PrintQueueActionCenter", "SecureAssessmentBrowser", "XboxGameCallableUI",
    ];

    // Bloatware name fragments, aligned with Win11Debloat's supported-apps list (matched as substrings
    // against the package Name). Genuinely useful apps (Calculator, Camera, Photos, Notepad, classic
    // Paint, Snipping Tool, Terminal, Sticky Notes) and game-required Xbox components (TCUI,
    // IdentityProvider, SpeechToText) are deliberately NOT flagged.
    private static readonly string[] Bloat =
    [
        // Microsoft first-party bloat (Bing suite, discontinued & ad apps)
        "BingNews", "BingWeather", "BingSearch", "BingFinance", "BingSports", "BingTranslator",
        "BingFoodAndDrink", "BingHealthAndFitness", "BingTravel", "Microsoft.News", "549981C3F5F10",
        "Clipchamp", "Copilot", "DevHome", "GetHelp", "Getstarted", "Messaging", "MicrosoftOfficeHub",
        "SolitaireCollection", "Microsoft.People", "PowerAutomate", "Todos", "windowscommunicationsapps",
        "FeedbackHub", "WindowsMaps", "SoundRecorder", "YourPhone", "ZuneMusic", "ZuneVideo",
        "MixedReality", "3DBuilder", "Microsoft3DViewer", "Print3D", "MSPaint", "SkypeApp", "Whiteboard",
        "MicrosoftJournal", "NetworkSpeedTest", "OneConnect", "OutlookForWindows", "MSTeams", "MicrosoftTeams",
        "MicrosoftFamily", "QuickAssist", "PCManager", "Office.Sway", "Office.OneNote",
        "MicrosoftPowerBIForWindows", "Microsoft.Wallet", "M365Companions",
        // Xbox overlays / apps (game-required Xbox components are excluded above)
        "XboxGamingOverlay", "XboxGameOverlay", "GamingApp", "XboxApp",
        // Common third-party preinstalls
        "king.com", "CandyCrush", "BubbleWitch", "Spotify", "Disney", "Netflix", "Facebook", "Twitter",
        "Instagram", "TikTok", "LinkedIn", "Amazon", "PrimeVideo", "Duolingo", "Wunderlist", "Flipboard",
        "Viber", "Shazam", "Plex", "Hulu", "iHeartRadio", "Pandora", "PicsArt", "Fitbit",
        "AdobePhotoshopExpress", "DrawboardPDF", "WinZipUniversal", "Asphalt8", "MarchofEmpires", "FarmVille",
        "HiddenCity", "CookingFever", "CaesarsSlots", "NYTCrossword", "SlingTV", "TuneIn",
        // OEM bloat (HP / Dell / Lenovo / CyberLink / Actipro)
        "AD2F1837", "DellInc", "LenovoCompanion", "LenovoVantage", "E046963F", "CyberLink", "ActiproSoftware",
    ];

    public Task<List<AppPackage>> GetPackagesAsync() => Task.Run(() =>
    {
        var result = new List<AppPackage>();
        // -AllUsers (the app runs elevated) so apps installed for any user are listed and can be
        // removed for everyone, matching Win11Debloat's all-users removal model.
        string json = RunPowerShell(
            "Get-AppxPackage -AllUsers | Select-Object Name,PackageFullName,Publisher,NonRemovable,IsFramework,SignatureKind | ConvertTo-Json -Compress");
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            IEnumerable<JsonElement> elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : SingleArray(doc.RootElement);

            foreach (var el in elements)
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                string name = GetStr(el, "Name");
                string full = GetStr(el, "PackageFullName");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(full)) continue;

                bool isFramework = GetBool(el, "IsFramework");
                string sig = GetStr(el, "SignatureKind");
                if (isFramework) continue;
                if (sig.Equals("System", StringComparison.OrdinalIgnoreCase)) continue;
                // GUID-named packages are Windows system experience hosts, never user apps.
                if (Guid.TryParse(name, out _)) continue;
                if (Protected.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

                result.Add(new AppPackage
                {
                    Name = name,
                    PackageFullName = full,
                    Publisher = GetStr(el, "Publisher"),
                    IsBloatware = Bloat.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase)),
                });
            }
        }
        catch (Exception) { }

        return result
            .DistinctBy(p => p.PackageFullName)
            .OrderByDescending(p => p.IsBloatware)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    public Task<(bool Ok, string Message)> UninstallAsync(AppPackage package) => Task.Run(() =>
    {
        try
        {
            // Win11Debloat-style thorough removal: uninstall for every user AND remove the provisioned
            // package so it doesn't reinstall for new users. Verify it's actually gone for the exit code.
            string name = package.Name.Replace("'", "''");
            string command =
                $"$n='{name}'; " +
                "Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; " +
                "Get-AppxProvisionedPackage -Online | Where-Object DisplayName -EQ $n | " +
                "Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue; " +
                "if (Get-AppxPackage -AllUsers -Name $n) { exit 1 } else { exit 0 }";

            int exit = RunPowerShellExit(command);
            return exit == 0
                ? (true, $"Removed {package.DisplayName} for all users.")
                : (false, $"Could not fully remove {package.DisplayName} (it may be a protected system app).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    });

    public Task<List<(AppPackage Package, bool Ok)>> UninstallManyAsync(
        IReadOnlyList<AppPackage> packages, Action<AppPackage>? onProgress = null) => Task.Run(() =>
    {
        var results = new List<(AppPackage, bool)>();
        if (packages.Count == 0) return results;

        // Map by Name so streamed "RESULT|<name>|OK" lines route back to the original package
        // (and drive the progress callback). Names are unique per removable app in this list.
        var byName = new Dictionary<string, AppPackage>(StringComparer.OrdinalIgnoreCase);
        var arrayItems = new List<string>();
        foreach (var p in packages)
        {
            byName[p.Name] = p;
            arrayItems.Add("'" + p.Name.Replace("'", "''") + "'");
        }

        // ONE PowerShell process handles every app: for each name, remove for all users + remove the
        // provisioned package, then verify and emit a machine-readable result line we parse as it streams.
        string script =
            "$names=@(" + string.Join(",", arrayItems) + "); " +
            "foreach ($n in $names) { " +
            "  Write-Output \"BEGIN|$n\"; " +
            "  try { " +
            "    Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; " +
            "    Get-AppxProvisionedPackage -Online | Where-Object DisplayName -EQ $n | " +
            "      Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue; " +
            "  } catch {} " +
            "  if (Get-AppxPackage -AllUsers -Name $n) { Write-Output \"RESULT|$n|FAIL\" } " +
            "  else { Write-Output \"RESULT|$n|OK\" } " +
            "}";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (p is null) return results;

            string? line;
            while ((line = p.StandardOutput.ReadLine()) is not null)
            {
                if (line.StartsWith("BEGIN|", StringComparison.Ordinal))
                {
                    string n = line[6..];
                    if (onProgress is not null && byName.TryGetValue(n, out var pk)) onProgress(pk);
                }
                else if (line.StartsWith("RESULT|", StringComparison.Ordinal))
                {
                    int bar = line.LastIndexOf('|');
                    string n = line[7..bar];
                    bool ok = line[(bar + 1)..].Equals("OK", StringComparison.OrdinalIgnoreCase);
                    if (byName.TryGetValue(n, out var pk) && seen.Add(n)) results.Add((pk, ok));
                }
            }
            p.StandardError.ReadToEnd();
            // Generous ceiling: many apps removed sequentially in one process.
            p.WaitForExit(Math.Max(120000, packages.Count * 30000));
        }
        catch (Exception) { }

        // Any app that produced no result line (e.g. the process died early) counts as failed.
        foreach (var p in packages)
            if (!seen.Contains(p.Name)) results.Add((p, false));

        return results;
    });

    private static IEnumerable<JsonElement> SingleArray(JsonElement obj)
    {
        yield return obj;
    }

    private static string GetStr(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool GetBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True ||
            (v.ValueKind == JsonValueKind.Number && v.GetInt32() != 0));

    private static string RunPowerShell(string command)
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
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (p is null) return "";
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);
            return output;
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static int RunPowerShellExit(string command)
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
        p.WaitForExit(60000);
        return p.ExitCode;
    }
}
