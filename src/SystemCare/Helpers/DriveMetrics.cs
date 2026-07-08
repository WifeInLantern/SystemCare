using SystemCare.Models;

namespace SystemCare.Helpers;

public static class DriveMetrics
{
    /// <summary>
    /// Free space on the system drive (the one Windows is installed on) as a percentage, read from a
    /// snapshot's drive list. Returns 0 when the system drive isn't in the list (e.g. an empty snapshot in
    /// tests) so the health score treats it as "unknown" rather than penalising it.
    /// </summary>
    public static double SystemDriveFreePercent(IReadOnlyList<DriveStat> drives)
    {
        if (drives is null || drives.Count == 0) return 0;

        string systemLetter = Environment.SystemDirectory.Length > 0
            ? Environment.SystemDirectory[0].ToString()
            : "C";

        var drive = drives.FirstOrDefault(d =>
            d.Name.StartsWith(systemLetter, StringComparison.OrdinalIgnoreCase));

        return drive is not null && drive.TotalBytes > 0
            ? drive.FreeBytes * 100.0 / drive.TotalBytes
            : 0;
    }
}
