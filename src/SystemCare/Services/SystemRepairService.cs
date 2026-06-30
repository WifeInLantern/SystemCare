using System.Diagnostics;
using System.Text;

namespace SystemCare.Services;

public enum RepairOutcome
{
    Healthy,
    Repaired,
    NeedsAttention,
    ScheduledForRestart,
    Unknown,
}

public record RepairResult(int ExitCode, RepairOutcome Outcome, string Summary);

public interface ISystemRepairService
{
    /// <summary>Runs `sfc /scannow`. Streams output line by line via <paramref name="onOutput"/>.</summary>
    Task<RepairResult> RunSfcAsync(Action<string> onOutput, CancellationToken ct);

    /// <summary>Runs `DISM /Online /Cleanup-Image /RestoreHealth`. Streams output line by line.</summary>
    Task<RepairResult> RunDismAsync(Action<string> onOutput, CancellationToken ct);

    /// <summary>
    /// Runs `chkdsk {driveLetter} /f /r`. The target volume usually can't be locked while Windows is
    /// running, so this answers "Y" to the "schedule on next restart" prompt automatically.
    /// </summary>
    Task<RepairResult> RunChkdskRepairAsync(string driveLetter, Action<string> onOutput, CancellationToken ct);
}

/// <summary>
/// Wraps the same process-streaming primitive <c>DiskHealthViewModel</c> already uses for SFC/DISM
/// (<see cref="IDiskMaintenanceService.RunAsync"/>) with a guided "run all three" repair sequence and
/// plain-language result summaries. CHKDSK repair mode (/f /r) launches its own process because it needs
/// stdin redirected to auto-answer the "schedule on next restart?" prompt.
/// </summary>
public class SystemRepairService(IDiskMaintenanceService disk) : ISystemRepairService
{
    public async Task<RepairResult> RunSfcAsync(Action<string> onOutput, CancellationToken ct)
    {
        var captured = new StringBuilder();
        int exit = await disk.RunAsync("sfc", "/scannow", Capture(onOutput, captured), Encoding.Unicode, ct);
        var (outcome, summary) = SummarizeSfc(exit, captured.ToString());
        return new RepairResult(exit, outcome, summary);
    }

    public async Task<RepairResult> RunDismAsync(Action<string> onOutput, CancellationToken ct)
    {
        var captured = new StringBuilder();
        int exit = await disk.RunAsync("Dism.exe", "/Online /Cleanup-Image /RestoreHealth", Capture(onOutput, captured), null, ct);
        var (outcome, summary) = SummarizeDism(exit, captured.ToString());
        return new RepairResult(exit, outcome, summary);
    }

    public async Task<RepairResult> RunChkdskRepairAsync(string driveLetter, Action<string> onOutput, CancellationToken ct)
    {
        var captured = new StringBuilder();
        int exit = await RunChkdskWithAutoConfirmAsync(driveLetter, Capture(onOutput, captured), ct);
        var (outcome, summary) = SummarizeChkdsk(exit, captured.ToString());
        return new RepairResult(exit, outcome, summary);
    }

    private static Action<string> Capture(Action<string> onOutput, StringBuilder sink) => line =>
    {
        sink.AppendLine(line);
        onOutput(line);
    };

    /// <summary>
    /// Pure summarizer — exit code is the primary signal; output-text phrases are a secondary,
    /// locale-dependent hint (sfc.exe's English-language wording isn't a stable contract).
    /// </summary>
    public static (RepairOutcome Outcome, string Summary) SummarizeSfc(int exitCode, string output)
    {
        if (exitCode != 0)
            return (RepairOutcome.Unknown, $"SFC exited with code {exitCode} — see the log for details.");
        if (output.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
            return (RepairOutcome.Healthy, "No integrity violations found.");
        if (output.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase))
            return (RepairOutcome.Repaired, "Corrupt files were found and repaired.");
        if (output.Contains("unable to fix", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("was not able to fix", StringComparison.OrdinalIgnoreCase))
            return (RepairOutcome.NeedsAttention, "Corrupt files were found but some couldn't be fixed automatically.");
        return (RepairOutcome.Unknown, "Scan finished — check the output for details.");
    }

    public static (RepairOutcome Outcome, string Summary) SummarizeDism(int exitCode, string output)
    {
        if (exitCode != 0)
            return (RepairOutcome.NeedsAttention, $"DISM exited with code {exitCode} — the image may still be damaged.");
        if (output.Contains("no component store corruption detected", StringComparison.OrdinalIgnoreCase))
            return (RepairOutcome.Healthy, "No component store corruption detected.");
        if (output.Contains("restore operation completed successfully", StringComparison.OrdinalIgnoreCase))
            return (RepairOutcome.Repaired, "The Windows image was repaired.");
        return (RepairOutcome.Unknown, "Repair finished — check the output for details.");
    }

    public static (RepairOutcome Outcome, string Summary) SummarizeChkdsk(int exitCode, string output)
    {
        if (output.Contains("scheduled", StringComparison.OrdinalIgnoreCase) &&
            output.Contains("restart", StringComparison.OrdinalIgnoreCase))
            return (RepairOutcome.ScheduledForRestart, "The drive is in use, so the check is scheduled for the next restart.");
        if (exitCode != 0)
            return (RepairOutcome.NeedsAttention, $"CHKDSK exited with code {exitCode} — see the output for details.");
        if (output.Contains("found no problems", StringComparison.OrdinalIgnoreCase))
            return (RepairOutcome.Healthy, "No problems found.");
        return (RepairOutcome.Repaired, "CHKDSK finished — see the output for details.");
    }

    private static async Task<int> RunChkdskWithAutoConfirmAsync(string driveLetter, Action<string> onOutput, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c chkdsk {driveLetter} /f /r")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Environment.SystemDirectory,
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
            onOutput($"Could not start chkdsk: {ex.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Answers the "schedule on next restart?" prompt; harmless if the volume wasn't locked.
        try
        {
            await process.StandardInput.WriteLineAsync("Y");
            process.StandardInput.Close();
        }
        catch (Exception) { }

        await using var reg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        return process.ExitCode;
    }
}
