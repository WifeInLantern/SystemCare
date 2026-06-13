using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SystemCare.Services;

public record PowerScheme(Guid Guid, string Name);

public interface IPowerPlanService
{
    List<PowerScheme> ListSchemes();
    Guid? GetActiveScheme();
    bool SetActiveScheme(Guid guid);

    /// <summary>Built-in High Performance scheme GUID.</summary>
    Guid HighPerformanceGuid { get; }
}

public class PowerPlanService : IPowerPlanService
{
    public Guid HighPerformanceGuid { get; } = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private static readonly Regex SchemeLine =
        new(@"GUID:\s*([0-9a-fA-F-]{36})\s*\(([^)]*)\)", RegexOptions.Compiled);

    public List<PowerScheme> ListSchemes()
    {
        var schemes = new List<PowerScheme>();
        foreach (Match m in SchemeLine.Matches(RunPowercfg("/list")))
        {
            if (Guid.TryParse(m.Groups[1].Value, out var g))
                schemes.Add(new PowerScheme(g, m.Groups[2].Value.Trim()));
        }
        return schemes;
    }

    public Guid? GetActiveScheme()
    {
        var m = SchemeLine.Match(RunPowercfg("/getactivescheme"));
        return m.Success && Guid.TryParse(m.Groups[1].Value, out var g) ? g : null;
    }

    public bool SetActiveScheme(Guid guid)
    {
        try
        {
            RunPowercfg($"/setactive {guid}");
            return GetActiveScheme() == guid;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string RunPowercfg(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c powercfg {args}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Environment.SystemDirectory,
            });
            if (p is null) return "";
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return output;
        }
        catch (Exception)
        {
            return "";
        }
    }
}
