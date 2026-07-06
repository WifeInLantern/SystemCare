using System.Diagnostics;
using System.Text;

namespace SystemCare.Helpers;

/// <summary>
/// Small shared helper for running a console tool (netsh, powershell, schtasks…) with no window and
/// capturing its combined stdout+stderr. Used by services that wrap Windows command-line utilities.
/// </summary>
public static class ProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return (-1, $"Could not start {fileName}: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var reg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { return (-1, sb.ToString()); }

        return (process.ExitCode, sb.ToString());
    }

    /// <summary>Runs a PowerShell command non-interactively and returns its combined output.</summary>
    public static Task<(int ExitCode, string Output)> RunPowerShellAsync(string command, CancellationToken ct = default)
        => RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"", ct);
}
