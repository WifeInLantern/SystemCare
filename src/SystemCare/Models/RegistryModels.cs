using Microsoft.Win32;

namespace SystemCare.Models;

public class RegistryCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public class RegistryIssue
{
    public required string CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required RegistryHive Hive { get; init; }
    public required RegistryView View { get; init; }
    /// <summary>Subkey path under the hive that holds the issue (the key we open writable).</summary>
    public required string SubKeyPath { get; init; }
    /// <summary>Value to delete, or null to delete the whole subkey (orphan key).</summary>
    public string? ValueName { get; init; }
    public string Data { get; init; } = "";
    public string Reason { get; init; } = "";

    /// <summary>Human-readable location for the UI, e.g. HKLM\Software\...\Run\Foo.</summary>
    public string DisplayPath
    {
        get
        {
            string root = Hive switch
            {
                RegistryHive.LocalMachine => "HKLM",
                RegistryHive.CurrentUser => "HKCU",
                RegistryHive.ClassesRoot => "HKCR",
                RegistryHive.Users => "HKU",
                _ => Hive.ToString(),
            };
            string path = $@"{root}\{SubKeyPath}";
            return ValueName is null ? path : $@"{path}\\{ValueName}";
        }
    }

    /// <summary>The full key path used for `reg export`, e.g. HKLM\Software\...\App Paths\foo.exe.</summary>
    public string ExportKeyPath
    {
        get
        {
            string root = Hive switch
            {
                RegistryHive.LocalMachine => "HKLM",
                RegistryHive.CurrentUser => "HKCU",
                RegistryHive.ClassesRoot => "HKCR",
                RegistryHive.Users => "HKU",
                _ => Hive.ToString(),
            };
            return $@"{root}\{SubKeyPath}";
        }
    }
}

public class RegistryCleanResult
{
    public int Removed { get; set; }
    public int Skipped { get; set; }
    public string BackupFolder { get; set; } = "";
}
