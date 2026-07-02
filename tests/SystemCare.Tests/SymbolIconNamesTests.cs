using System.IO;
using System.Text.RegularExpressions;
using SystemCare.Helpers;
using SystemCare.Models;
using Wpf.Ui.Controls;
using Xunit;

namespace SystemCare.Tests;

/// <summary>
/// Invalid <c>SymbolRegular</c> names in XAML compile fine but crash (or silently blank) at BAML
/// load — the same failure class as the 2.3.6 <c>SelectionMode="None"</c> dialog crash. This scans
/// every page/window XAML for literal <c>Symbol="…"</c> values and checks them against the real
/// enum, and does the same for the icon names services supply as strings at runtime.
/// </summary>
public class SymbolIconNamesTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SystemCare.sln")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void EveryLiteralSymbolInXaml_IsAValidSymbolRegularName()
    {
        string appDir = Path.Combine(FindRepoRoot(), "src", "SystemCare");
        var bad = new List<string>();

        foreach (string file in Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "Symbol=\"(?<name>[A-Za-z0-9]+)\""))
            {
                string name = m.Groups["name"].Value;
                if (!Enum.TryParse<SymbolRegular>(name, out _))
                    bad.Add($"{Path.GetFileName(file)}: {name}");
            }
        }

        Assert.Empty(bad);
    }

    [Fact]
    public void RecommendationBuilderIcons_AreValidSymbolNames()
    {
        // Force every rule to fire so every icon string is produced.
        var health = new Services.HealthScoreService();
        var junk = new JunkScanResult();
        junk.Categories.Add(new JunkCategoryResult
        {
            Category = new JunkCategory { Id = "t", Name = "t", Description = "" },
            TotalBytes = 4L * 1024 * 1024 * 1024,
            FileCount = 1,
        });
        var probes = new AutoCareProbeResults
        {
            Junk = junk,
            EnabledStartupItems = 12,
            RamLoadPercent = 95,
            SecurityIssues = 2,
            PendingSoftwareUpdates = 6,
            Health = health.Compute(new HealthInputs
            {
                JunkBytes = 4L * 1024 * 1024 * 1024,
                EnabledStartupItems = 12,
                RamLoadPercent = 95,
                SecurityIssues = 2,
            }),
        };

        var recs = RecommendationBuilder.Build(probes);

        Assert.Equal(5, recs.Count); // all five rules fired
        Assert.All(recs, r => Assert.True(Enum.TryParse<SymbolRegular>(r.Icon, out _), $"bad icon: {r.Icon}"));
    }
}
