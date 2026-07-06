namespace SystemCare.Models;

/// <summary>A non-Microsoft Windows scheduled task shown in the Scheduled Tasks manager.</summary>
public record ScheduledTaskInfo(string Path, string Name, string Folder, bool Enabled, string State, string Author);
