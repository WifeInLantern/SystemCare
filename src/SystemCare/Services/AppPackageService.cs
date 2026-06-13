using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SystemCare.Models;

namespace SystemCare.Services;

public interface IAppPackageService
{
    Task<List<AppPackage>> GetPackagesAsync();
    Task<(bool Ok, string Message)> UninstallAsync(AppPackage package);
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

    // Names commonly considered bloatware (badged for quick selection).
    private static readonly string[] Bloat =
    [
        "BingWeather", "BingNews", "BingFinance", "BingSports", "Bing.Search", "Xbox", "GamingApp",
        "ZuneMusic", "ZuneVideo", "Microsoft.People", "windowscommunicationsapps", "SolitaireCollection",
        "Clipchamp", "Todos", "PowerAutomate", "Teams", "YourPhone", "Windows.Phone", "GetHelp",
        "Getstarted", "MixedReality", "OneConnect", "FeedbackHub", "WindowsMaps", "3DBuilder",
        "MicrosoftOfficeHub", "SkypeApp", "Disney", "Spotify", "Netflix", "CandyCrush", "Facebook",
        "Twitter", "Instagram", "Wunderlist", "Microsoft.OneNote", "MSPaint", "Print3D", "Office.OneNote",
        "Microsoft.Advertising", "Dolby", "MicrosoftSolitaire", "Microsoft.Messaging",
    ];

    public Task<List<AppPackage>> GetPackagesAsync() => Task.Run(() =>
    {
        var result = new List<AppPackage>();
        string json = RunPowerShell(
            "Get-AppxPackage | Select-Object Name,PackageFullName,Publisher,NonRemovable,IsFramework,SignatureKind | ConvertTo-Json -Compress");
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
            .OrderByDescending(p => p.IsBloatware)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    public Task<(bool Ok, string Message)> UninstallAsync(AppPackage package) => Task.Run(() =>
    {
        try
        {
            int exit = RunPowerShellExit($"Remove-AppxPackage -Package '{package.PackageFullName}' -ErrorAction Stop");
            return exit == 0
                ? (true, $"Removed {package.DisplayName}.")
                : (false, $"Could not remove {package.DisplayName} (it may be provisioned for all users).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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
