using System.Diagnostics;
using System.Text;

namespace SystemCare.Helpers;

/// <summary>
/// Small shared helper for running a console tool (netsh, powershell, schtasks…) with no window and
/// capturing its combined stdout+stderr. Used by services that wrap Windows command-line utilities.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Hard ceiling for any external tool (2.19.4). Previously a wedged child process (a stuck
    /// netsh/DISM/chkdsk, or a Windows Search service that won't stop) left the await pending
    /// forever and silently froze the calling operation — six call sites passed no token at all.
    /// Every run is now bounded by construction; callers needing longer pass their own token.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Output cap (2.19.4): DISM/SFC on a damaged store can emit tens of MB. Keep the
    /// tail — the interesting part of a tool's output is almost always at the end.</summary>
    private const int MaxOutputChars = 1_000_000;

    public static Task<(int ExitCode, string Output)> RunAsync(
        string fileName, string arguments, CancellationToken ct = default)
        => RunAsync(fileName, arguments, DefaultTimeout, ct);

    public static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName, string arguments, TimeSpan timeout, CancellationToken ct)
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
        var gate = new object();

        void Append(string? data)
        {
            if (data is null) return;
            lock (gate)
            {
                sb.AppendLine(data);
                if (sb.Length > MaxOutputChars)
                    sb.Remove(0, sb.Length - MaxOutputChars); // keep the tail
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

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

        // The caller's token still cancels; the timeout is an additional, always-present bound.
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await using var reg = linked.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        string Captured() { lock (gate) return sb.ToString(); }

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Distinguish "the user cancelled" from "the tool hung", so callers can report honestly.
            return timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested
                ? (-1, Captured() + $"{Environment.NewLine}[SystemCare] {fileName} exceeded {timeout.TotalSeconds:0}s and was stopped.")
                : (-1, Captured());
        }

        return (process.ExitCode, Captured());
    }

    /// <summary>Runs a PowerShell command non-interactively and returns its combined output.</summary>
    public static Task<(int ExitCode, string Output)> RunPowerShellAsync(string command, CancellationToken ct = default)
        => RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"", ct);

    /// <summary>PowerShell with an explicit timeout (long-running maintenance commands).</summary>
    public static Task<(int ExitCode, string Output)> RunPowerShellAsync(string command, TimeSpan timeout, CancellationToken ct = default)
        => RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"", timeout, ct);
}
