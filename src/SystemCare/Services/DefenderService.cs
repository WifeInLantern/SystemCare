using System.Diagnostics;
using System.Management;
using System.Text;
using SystemCare.Models;

namespace SystemCare.Services;

/// <summary>Outcome of an MpCmdRun scan. ExitCode 0 == no threats, 2 == threats found & handled.</summary>
public record DefenderScanResult(int ExitCode, bool ThreatsFound, string Summary);

public interface IDefenderService
{
    /// <summary>Reads the current Defender protection status via WMI.</summary>
    Task<DefenderStatus> GetStatusAsync();

    /// <summary>Runs a quick or full scan through MpCmdRun.exe, streaming console output.</summary>
    Task<DefenderScanResult> StartScanAsync(DefenderScanType scanType, Action<string> onOutput, CancellationToken ct);

    /// <summary>Pulls the latest antivirus signature/definition update through MpCmdRun.exe.</summary>
    Task<DefenderScanResult> UpdateSignaturesAsync(Action<string> onOutput, CancellationToken ct);

    /// <summary>Opens the Windows Security app (Virus &amp; threat protection).</summary>
    void OpenWindowsSecurity();
}

public class DefenderService : IDefenderService
{
    private readonly ILogService _log;

    public DefenderService(ILogService log) => _log = log;

    public Task<DefenderStatus> GetStatusAsync() => Task.Run(() =>
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM MSFT_MpComputerStatus"));

            foreach (ManagementBaseObject mo in searcher.Get())
            {
                bool av = mo["AntivirusEnabled"] as bool? ?? false;
                bool rtp = mo["RealTimeProtectionEnabled"] as bool? ?? false;
                bool tamper = mo["IsTamperProtected"] as bool? ?? false;
                string sig = mo["AntivirusSignatureVersion"] as string ?? "";
                int age = (int)(mo["AntivirusSignatureAge"] as uint? ?? 0u);

                string headline = (av, rtp) switch
                {
                    (true, true) => "Defender is protecting this PC in real time.",
                    (true, false) => "Antivirus is on, but real-time protection is off.",
                    _ => "Defender protection is off (another antivirus may be active).",
                };

                return new DefenderStatus
                {
                    IsAvailable = true,
                    AntivirusEnabled = av,
                    RealTimeProtectionEnabled = rtp,
                    TamperProtectionEnabled = tamper,
                    AntivirusSignatureVersion = sig,
                    SignatureAgeDays = age,
                    LastQuickScan = ReadDate(mo, "QuickScanEndTime"),
                    LastFullScan = ReadDate(mo, "FullScanEndTime"),
                    Headline = headline,
                };
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Defender", $"Could not read Defender status: {ex.Message}");
        }

        return new DefenderStatus
        {
            IsAvailable = false,
            Headline = "Could not read Defender status — another antivirus may manage protection on this PC.",
        };
    });

    private static DateTime? ReadDate(ManagementBaseObject mo, string field)
    {
        try
        {
            if (mo[field] is string s && !string.IsNullOrWhiteSpace(s))
                return ManagementDateTimeConverter.ToDateTime(s);
        }
        catch (Exception) { }
        return null;
    }

    public Task<DefenderScanResult> StartScanAsync(DefenderScanType scanType, Action<string> onOutput, CancellationToken ct)
    {
        int type = (int)scanType;
        string label = scanType == DefenderScanType.Quick ? "Quick scan" : "Full scan";
        return RunMpCmdRunAsync($"-Scan -ScanType {type}", label, onOutput, ct);
    }

    public Task<DefenderScanResult> UpdateSignaturesAsync(Action<string> onOutput, CancellationToken ct)
        => RunMpCmdRunAsync("-SignatureUpdate", "Definition update", onOutput, ct);

    private async Task<DefenderScanResult> RunMpCmdRunAsync(string arguments, string label, Action<string> onOutput, CancellationToken ct)
    {
        string? exe = ResolveMpCmdRun();
        if (exe is null)
        {
            onOutput("Could not locate MpCmdRun.exe — Microsoft Defender may not be installed or is managed by another antivirus.");
            return new DefenderScanResult(-1, false, "Defender command-line tool not found.");
        }

        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            onOutput($"Could not start MpCmdRun.exe: {ex.Message}");
            return new DefenderScanResult(-1, false, $"{label} could not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var reg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return new DefenderScanResult(-1, false, $"{label} cancelled.");
        }

        int code = process.ExitCode;
        // MpCmdRun returns 0 when clean and 2 when malware was found and remediated.
        bool threats = code == 2;
        string summary = code switch
        {
            0 => $"{label} complete — no threats found.",
            2 => $"{label} complete — threats were detected and handled. Open Windows Security for details.",
            _ => $"{label} finished with code {code}.",
        };
        _log.Info("Defender", summary);
        return new DefenderScanResult(code, threats, summary);
    }

    /// <summary>
    /// MpCmdRun ships in the versioned Platform folder (kept current by updates) and, as a fallback,
    /// the classic Program Files location. Prefer the newest Platform build when present.
    /// </summary>
    private static string? ResolveMpCmdRun()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string platformRoot = Path.Combine(programData, "Microsoft", "Windows Defender", "Platform");
            if (Directory.Exists(platformRoot))
            {
                var newest = new DirectoryInfo(platformRoot).GetDirectories()
                    .OrderByDescending(d => d.Name)
                    .Select(d => Path.Combine(d.FullName, "MpCmdRun.exe"))
                    .FirstOrDefault(File.Exists);
                if (newest is not null) return newest;
            }
        }
        catch (Exception) { }

        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        })
        {
            try
            {
                string candidate = Path.Combine(folder, "Windows Defender", "MpCmdRun.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception) { }
        }
        return null;
    }

    public void OpenWindowsSecurity()
    {
        try
        {
            Process.Start(new ProcessStartInfo("windowsdefender://threat") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn("Defender", $"Could not open Windows Security: {ex.Message}");
        }
    }
}
