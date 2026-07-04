using SystemCare.Models;

namespace SystemCare.Services;

/// <summary>Hardcoded catalog of popular free Windows apps offered by the Software Hub — a
/// Ninite-style, one-click bulk installer built on winget. Adding an app requires a code change +
/// release (the catalog is not fetched remotely). Every <see cref="SoftwareHubApp.Id"/> must be a
/// valid winget package id from the <c>winget</c> source (verify with
/// <c>tools\verify-softwarehub-ids.cmd</c>), since installs run <c>winget install --id &lt;Id&gt; --exact</c>.</summary>
internal static class SoftwareHubCatalog
{
    public const string Browsers = "Web Browsers";
    public const string Communication = "Messaging";
    public const string Media = "Media";
    public const string Imaging = "Imaging";
    public const string Documents = "Documents";
    public const string DeveloperTools = "Developer Tools";
    public const string Utilities = "Utilities";
    public const string Compression = "Compression";
    public const string Security = "Security";
    public const string Cloud = "Online Storage";
    public const string Runtimes = "Runtimes";
    public const string FileSharing = "File Sharing";

    public static readonly IReadOnlyList<SoftwareHubApp> All =
    [
        // ── Web Browsers ────────────────────────────────────────────────────────────
        new() { Name = "Mozilla Firefox", Id = "Mozilla.Firefox", Category = Browsers, Description = "Fast, private web browser." },
        new() { Name = "Google Chrome", Id = "Google.Chrome", Category = Browsers, Description = "Google's web browser." },
        new() { Name = "Brave", Id = "Brave.Brave", Category = Browsers, Description = "Privacy-focused browser with built-in ad blocking." },
        new() { Name = "Opera", Id = "Opera.Opera", Category = Browsers, Description = "Browser with a built-in free VPN." },
        new() { Name = "Vivaldi", Id = "Vivaldi.Vivaldi", Category = Browsers, Description = "Highly customizable power-user browser." },

        // ── Messaging ───────────────────────────────────────────────────────────────
        new() { Name = "Discord", Id = "Discord.Discord", Category = Communication, Description = "Voice, video and text chat for communities." },
        new() { Name = "Zoom", Id = "Zoom.Zoom", Category = Communication, Description = "Video conferencing." },
        new() { Name = "Slack", Id = "SlackTechnologies.Slack", Category = Communication, Description = "Team messaging." },
        new() { Name = "Microsoft Teams", Id = "Microsoft.Teams", Category = Communication, Description = "Team chat and meetings." },
        new() { Name = "Telegram", Id = "Telegram.TelegramDesktop", Category = Communication, Description = "Cloud-based instant messaging." },
        new() { Name = "Mozilla Thunderbird", Id = "Mozilla.Thunderbird", Category = Communication, Description = "Free email client." },

        // ── Media ───────────────────────────────────────────────────────────────────
        new() { Name = "VLC media player", Id = "VideoLAN.VLC", Category = Media, Description = "Plays almost any video/audio format." },
        new() { Name = "Spotify", Id = "Spotify.Spotify", Category = Media, Description = "Music streaming." },
        new() { Name = "OBS Studio", Id = "OBSProject.OBSStudio", Category = Media, Description = "Free screen recording and live streaming." },
        new() { Name = "Audacity", Id = "Audacity.Audacity", Category = Media, Description = "Multi-track audio recorder and editor." },
        new() { Name = "HandBrake", Id = "HandBrake.HandBrake", Category = Media, Description = "Video transcoder / converter." },
        new() { Name = "foobar2000", Id = "PeterPawlowski.foobar2000", Category = Media, Description = "Lightweight, customizable audio player." },
        new() { Name = "AIMP", Id = "AIMP.AIMP", Category = Media, Description = "Feature-rich audio player." },
        new() { Name = "MPC-HC", Id = "clsid2.mpc-hc", Category = Media, Description = "Lightweight media player (Media Player Classic)." },
        new() { Name = "K-Lite Codec Pack", Id = "CodecGuide.K-LiteCodecPack.Standard", Category = Media, Description = "Codecs for playing most media files." },

        // ── Imaging ─────────────────────────────────────────────────────────────────
        new() { Name = "GIMP", Id = "GIMP.GIMP", Category = Imaging, Description = "Full-featured raster image editor." },
        new() { Name = "Krita", Id = "KDE.Krita", Category = Imaging, Description = "Digital painting and illustration." },
        new() { Name = "Blender", Id = "BlenderFoundation.Blender", Category = Imaging, Description = "3D modeling, animation and rendering." },
        new() { Name = "Paint.NET", Id = "dotPDN.PaintDotNet", Category = Imaging, Description = "Easy image and photo editing." },
        new() { Name = "IrfanView", Id = "IrfanSkiljan.IrfanView", Category = Imaging, Description = "Fast, lightweight image viewer." },
        new() { Name = "Inkscape", Id = "Inkscape.Inkscape", Category = Imaging, Description = "Vector graphics editor (SVG)." },
        new() { Name = "ShareX", Id = "ShareX.ShareX", Category = Imaging, Description = "Screen capture, recording and sharing." },
        new() { Name = "Greenshot", Id = "Greenshot.Greenshot", Category = Imaging, Description = "Lightweight screenshot tool." },
        new() { Name = "XnView MP", Id = "XnSoft.XnViewMP", Category = Imaging, Description = "Image viewer, organizer and converter." },

        // ── Documents ───────────────────────────────────────────────────────────────
        new() { Name = "LibreOffice", Id = "TheDocumentFoundation.LibreOffice", Category = Documents, Description = "Free office suite (Writer, Calc, Impress)." },
        new() { Name = "SumatraPDF", Id = "SumatraPDF.SumatraPDF", Category = Documents, Description = "Very lightweight PDF/ebook reader." },
        new() { Name = "Foxit PDF Reader", Id = "Foxit.FoxitReader", Category = Documents, Description = "Fast, full-featured PDF reader." },
        new() { Name = "Adobe Acrobat Reader", Id = "Adobe.Acrobat.Reader.64-bit", Category = Documents, Description = "The standard PDF viewer." },
        new() { Name = "Apache OpenOffice", Id = "Apache.OpenOffice", Category = Documents, Description = "Free office productivity suite." },

        // ── Developer Tools ─────────────────────────────────────────────────────────
        new() { Name = "Git", Id = "Git.Git", Category = DeveloperTools, Description = "Distributed version control." },
        new() { Name = "Visual Studio Code", Id = "Microsoft.VisualStudioCode", Category = DeveloperTools, Description = "Free source-code editor." },
        new() { Name = "Python 3.12", Id = "Python.Python.3.12", Category = DeveloperTools, Description = "Python programming language runtime." },
        new() { Name = "Notepad++", Id = "Notepad++.Notepad++", Category = DeveloperTools, Description = "Lightweight source-code and text editor." },
        new() { Name = "PuTTY", Id = "PuTTY.PuTTY", Category = DeveloperTools, Description = "SSH and telnet client." },
        new() { Name = "WinSCP", Id = "WinSCP.WinSCP", Category = DeveloperTools, Description = "SFTP/FTP client with a dual-pane UI." },
        new() { Name = "WinMerge", Id = "WinMerge.WinMerge", Category = DeveloperTools, Description = "Visual file and folder diff/merge." },

        // ── Utilities ───────────────────────────────────────────────────────────────
        new() { Name = "PowerToys", Id = "Microsoft.PowerToys", Category = Utilities, Description = "Microsoft's power-user productivity utilities." },
        new() { Name = "Everything", Id = "voidtools.Everything", Category = Utilities, Description = "Instant filename search." },
        new() { Name = "KeePassXC", Id = "KeePassXCTeam.KeePassXC", Category = Utilities, Description = "Offline password manager." },
        new() { Name = "TeamViewer", Id = "TeamViewer.TeamViewer", Category = Utilities, Description = "Remote desktop access and support." },
        new() { Name = "WinDirStat", Id = "WinDirStat.WinDirStat", Category = Utilities, Description = "Visual disk-usage analyzer." },
        new() { Name = "Revo Uninstaller", Id = "RevoUninstaller.RevoUninstaller", Category = Utilities, Description = "Thorough app uninstaller with leftover cleanup." },

        // ── Compression ─────────────────────────────────────────────────────────────
        new() { Name = "7-Zip", Id = "7zip.7zip", Category = Compression, Description = "Free file archiver with a high compression ratio." },
        new() { Name = "WinRAR", Id = "RARLab.WinRAR", Category = Compression, Description = "Archive manager (RAR/ZIP)." },
        new() { Name = "PeaZip", Id = "Giorgiotani.Peazip", Category = Compression, Description = "Open-source archive manager." },

        // ── Security ────────────────────────────────────────────────────────────────
        new() { Name = "Malwarebytes", Id = "Malwarebytes.Malwarebytes", Category = Security, Description = "On-demand malware scanner and remover." },

        // ── Online Storage ──────────────────────────────────────────────────────────
        new() { Name = "Dropbox", Id = "Dropbox.Dropbox", Category = Cloud, Description = "Cloud file sync and storage." },
        new() { Name = "Google Drive", Id = "Google.GoogleDrive", Category = Cloud, Description = "Google's cloud storage and sync." },
        new() { Name = "MEGA", Id = "Mega.MEGASync", Category = Cloud, Description = "Encrypted cloud storage sync." },

        // ── Runtimes ────────────────────────────────────────────────────────────────
        new() { Name = ".NET Desktop Runtime 8", Id = "Microsoft.DotNet.DesktopRuntime.8", Category = Runtimes, Description = "Runtime for .NET desktop apps." },
        new() { Name = "Java Runtime (JRE)", Id = "Oracle.JavaRuntimeEnvironment", Category = Runtimes, Description = "Run Java applications." },
        new() { Name = "Eclipse Temurin JDK 21", Id = "EclipseAdoptium.Temurin.21.JDK", Category = Runtimes, Description = "Open-source OpenJDK development kit." },

        // ── File Sharing ────────────────────────────────────────────────────────────
        new() { Name = "qBittorrent", Id = "qBittorrent.qBittorrent", Category = FileSharing, Description = "Open-source, ad-free BitTorrent client." },
    ];
}
