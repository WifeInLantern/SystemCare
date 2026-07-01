using SystemCare.Models;

namespace SystemCare.Services;

/// <summary>Hardcoded catalog of popular free Windows apps offered by the Software Hub. Adding an
/// app requires a code change + release (the catalog is not fetched remotely).</summary>
internal static class SoftwareHubCatalog
{
    public const string Browsers = "Browsers";
    public const string Utilities = "Utilities";
    public const string Media = "Media";
    public const string DeveloperTools = "Developer Tools";
    public const string Communication = "Communication";

    public static readonly IReadOnlyList<SoftwareHubApp> All =
    [
        new() { Name = "Mozilla Firefox", Id = "Mozilla.Firefox", Category = Browsers, Description = "Fast, private web browser." },
        new() { Name = "Google Chrome", Id = "Google.Chrome", Category = Browsers, Description = "Google's web browser." },
        new() { Name = "Brave Browser", Id = "BraveSoftware.BraveBrowser", Category = Browsers, Description = "Privacy-focused browser with built-in ad blocking." },
        new() { Name = "Opera", Id = "Opera.Opera", Category = Browsers, Description = "Browser with a built-in free VPN." },

        new() { Name = "7-Zip", Id = "7zip.7zip", Category = Utilities, Description = "Free file archiver with a high compression ratio." },
        new() { Name = "WinRAR", Id = "RARLab.WinRAR", Category = Utilities, Description = "Archive manager (RAR/ZIP)." },
        new() { Name = "Notepad++", Id = "Notepad++.Notepad++", Category = Utilities, Description = "Lightweight source-code and text editor." },
        new() { Name = "PowerToys", Id = "Microsoft.PowerToys", Category = Utilities, Description = "Microsoft's power-user productivity utilities." },
        new() { Name = "Everything", Id = "voidtools.Everything", Category = Utilities, Description = "Instant filename search." },

        new() { Name = "VLC media player", Id = "VideoLAN.VLC", Category = Media, Description = "Plays almost any video/audio format." },
        new() { Name = "Spotify", Id = "Spotify.Spotify", Category = Media, Description = "Music streaming." },
        new() { Name = "OBS Studio", Id = "OBSProject.OBSStudio", Category = Media, Description = "Free screen recording and live streaming." },
        new() { Name = "IrfanView", Id = "IrfanSkiljan.IrfanView", Category = Media, Description = "Fast, lightweight image viewer/editor." },

        new() { Name = "Git", Id = "Git.Git", Category = DeveloperTools, Description = "Distributed version control." },
        new() { Name = "Visual Studio Code", Id = "Microsoft.VisualStudioCode", Category = DeveloperTools, Description = "Free source-code editor." },
        new() { Name = "Python 3.12", Id = "Python.Python.3.12", Category = DeveloperTools, Description = "Python programming language runtime." },

        new() { Name = "Discord", Id = "Discord.Discord", Category = Communication, Description = "Voice, video and text chat for communities." },
        new() { Name = "Zoom", Id = "Zoom.Zoom", Category = Communication, Description = "Video conferencing." },
        new() { Name = "Slack", Id = "SlackTechnologies.Slack", Category = Communication, Description = "Team messaging." },
        new() { Name = "Microsoft Teams", Id = "Microsoft.Teams", Category = Communication, Description = "Team chat and meetings." },
    ];
}
