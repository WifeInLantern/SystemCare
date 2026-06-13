namespace SystemCare.Helpers;

public static class CommandLineParser
{
    /// <summary>
    /// Best-effort extraction of the executable path from a registry Run command
    /// string, e.g. "\"C:\Program Files\App\app.exe\" /silent" or
    /// "C:\Tools\tool.exe -x". Returns null when no existing file can be resolved.
    /// </summary>
    public static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());

        if (command.StartsWith('"'))
        {
            int closing = command.IndexOf('"', 1);
            if (closing > 1)
            {
                string quoted = command[1..closing];
                return File.Exists(quoted) ? quoted : null;
            }
            return null;
        }

        // Unquoted: try the whole string, then progressively shorter prefixes at spaces
        // ("C:\Program Files\App\app.exe -x" → try full, then up to each space).
        if (File.Exists(command)) return command;
        if (File.Exists(command + ".exe")) return command + ".exe";

        int index = command.Length;
        while ((index = command.LastIndexOf(' ', index - 1)) > 0)
        {
            string prefix = command[..index];
            if (File.Exists(prefix)) return prefix;
            if (File.Exists(prefix + ".exe")) return prefix + ".exe";
        }
        return null;
    }
}
