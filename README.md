<div align="center">

<img src="src/SystemCare/Assets/app.png" alt="SystemCare logo" width="120" />

# SystemCare

**A complete Windows maintenance suite - clean, optimize, analyze and protect your PC, in one neon-lit app.**

[![Latest release](https://img.shields.io/github/v/release/WifeInLantern/SystemCare?sort=semver&color=00e5ff&label=release)](https://github.com/WifeInLantern/SystemCare/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/WifeInLantern/SystemCare/total?color=00ffa3)](https://github.com/WifeInLantern/SystemCare/releases)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078d6)
![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)
![Language](https://img.shields.io/badge/C%23-WPF-239120)

</div>

---

SystemCare is a Windows system-maintenance desktop app in the spirit of Advanced SystemCare, built with
**C# + WPF on .NET 8** using [WPF-UI](https://github.com/lepoco/wpfui). It's themed as a **cyberpunk
"Night City"** interface - neon cyan + magenta on near-black, an always-on animated neon-grid backdrop,
glowing cards and charts, and bundled futuristic fonts (Orbitron / Rajdhani, OFL). Dark-only, by design.

It bundles **25+ tools** behind a single dashboard: junk cleaning, a PC health score, software & driver
updating, disk analysis, privacy cleanup, startup management, security checkups, automatic scheduled
maintenance, and more - all in one self-contained executable that needs no .NET install.

## Table of contents

- [Highlights](#highlights)
- [Features](#features)
- [Download & install](#download--install)
- [Requirements](#requirements)
- [Build from source](#build-from-source)
- [Packaging](#packaging)
- [Project layout](#project-layout)
- [Tech stack](#tech-stack)
- [Safety & privacy](#safety--privacy)
- [Auto-update & configuration](#auto-update--configuration)
- [Changelog](#changelog)
- [License](#license)
- [Disclaimer](#disclaimer)

## Highlights

- 🧹 **One dashboard, 25+ tools** - grouped into **Clean / Optimize / Analyze / Protect**.
- ✨ **Refined neon dashboard** - an animated health gauge, flowing live graphs, counting stats, colour-shifting drive bars and staggered entrances.
- 💯 **PC Health score** - a single 0-100 gauge from junk, startup load, memory pressure and **security posture**, with one-click **Fix all**.
- 📦 **Software Updater** - update all your installed apps via `winget`, with per-app ignore lists and "Update all".
- 🔧 **Driver Updater** - finds newer drivers through the Windows Update catalog and installs them in-app.
- ♻️ **Automatic maintenance** - schedule cleanup + RAM trim daily/weekly via a Windows scheduled task, with tray notifications.
- 🛡️ **Safety first** - dry-run scans, Recycle Bin deletes, restore points before risky actions, `.reg` backups, and reversible tweaks.
- 🧾 **Activity History & diagnostic logs** - every action is recorded; rolling logs live under `%AppData%\SystemCare\logs`.
- 🚀 **Self-contained** - a single `SystemCare.exe`, no runtime to install.

## Features

### 🏠 Dashboard
A refined, animated hero screen. The **health-score gauge** sweeps to its value with a glowing tip and
tick marks; live CPU and RAM readouts **count up smoothly** above **flowing sparkline graphs**; and
per-drive bars **glide** to their fill and shift colour (cyan -> magenta) as a disk fills. One-click
**Scan** and **Fix all** (clean junk + trim RAM, then re-score). The score also factors in your
**security posture** (Defender, firewall, UAC, Remote Desktop, Windows Update). Cards and tiles cascade
in with a gentle staggered reveal, and a customizable row of **quick-action tiles** (Scan & Fix, Free
RAM, Flush DNS, Empty Recycle Bin, Create restore point) sits below.

### 🧹 Clean
- **Junk Cleanup** - temp files, Windows Update cache, thumbnail cache, error reports, crash dumps,
  browser caches (Chrome/Edge/Firefox) and the Recycle Bin. Scans are always a dry-run; only
  whitelisted locations are touched; in-use and recently-used files are protected; junctions/symlinks
  are never followed.
- **Privacy Cleaner** - browser history/cookies/cache, recent-files & jump lists, Run-dialog history,
  recent documents, DNS-cache flush and clipboard. Running browsers are detected and their locked data skipped.
- **Duplicate Finder** - size to partial-hash (first 64 KB) to full XxHash128 pipeline; keeps at least one
  copy per group; deletes to the Recycle Bin.
- **File Shredder** - securely overwrite (1-7 passes) and delete files/folders. _Irreversible._
- **Registry Cleaner** - conservative scan for orphaned entries (uninstall leftovers, App Paths, Run keys,
  shared DLLs, MUI cache) pointing to missing files. Always exports a `.reg` backup first (with **Restore
  last backup**) and can make a restore point - fully reversible.
- **Empty Folder Finder** - find and remove recursively-empty folders (to the Recycle Bin).
- **App Cache Cleaner** *(2.14)* - purge regenerable, often multi-GB caches from apps (Discord, Slack,
  Teams, Spotify, NVIDIA/Steam shaders) and developer tools (npm, Yarn, pip, NuGet HTTP cache, Gradle).
  Dry-run scan first; files from the last 24h and in-use files are always left alone.

### ⚡ Optimize
- **One-click Boost** - switches to the High Performance power plan, frees RAM, and can pause selected
  background apps (reversible); one-click **Restore**.
- **Startup Manager** - registry `Run` keys (incl. WOW6432Node), startup folders and scheduled tasks.
  Toggling uses the same `StartupApproved` mechanism as Task Manager, so entries are never deleted.
- **Windows Tweaks** - reversible toggles for visual effects, telemetry, Explorer (show extensions/hidden,
  classic context menu), startup delay, and a power-plan switcher.
- **Disk Health & Maintenance** - SMART health per disk, plus CHKDSK / optimize / SFC / DISM with live
  streaming output.
- **Deep Windows Cleanup** - reclaim big space: WinSxS component store (DISM), Windows.old, Delivery
  Optimization, Windows Update cache, and upgrade/setup leftovers, with a live console.
- **Driver Updater** - inventories every device + its current driver (`Win32_PnPSignedDriver`), flags
  problem/missing drivers, and checks the **Windows Update** driver catalog for newer drivers -
  downloading and installing selected ones in-app (a restore point is created first).
- **Software Updater** - checks installed apps for updates via the Windows Package Manager (**winget**),
  lets you pick exactly what to update, **Ignore** apps you never want touched, or **Update all** at once.
  A restore point is created first and every result is logged.

### 📊 Analyze
- **Disk Analyzer** - squarified treemap of a drive or folder with drill-down + breadcrumbs, plus a top-N
  largest-files list. Delete sends files to the Recycle Bin.
- **Processes & Services** - live process list (RAM, CPU%, end task) and Windows services with
  start/stop and start-mode control.
- **Network Tools** - active connections per process (via the TCP table), ping/traceroute, and
  flush/renew DNS & IP.
- **System Info & monitor** - CPU/GPU/motherboard/RAM/disk specs (via WMI) with **per-component
  temperatures** (LibreHardwareMonitor), plus live CPU, RAM and network sparkline graphs.
- **Activity History** - a running log of what SystemCare cleaned, freed and updated over time.

### 🛡️ Protect
- **Bloatware & Store-app remover** - list and uninstall AppX/UWP apps; system-critical and framework
  packages are hidden so only safe apps are removable.
- **Security Checkup** - Defender, Firewall, UAC, Remote Desktop and Windows Update status with quick-fix links.
- **Rescue Center** - create/list System Restore points and open System Restore; a restore point is created
  automatically before "Fix all" and disk maintenance.
- **Software Uninstaller** - lists installed programs from the registry, runs each program's own
  uninstaller, then scans for and offers to remove **leftover** files, folders and registry entries
  (with guards against generic-name false matches).

### ♻️ Automation & system tray
Optional **scheduled maintenance** (junk cleanup + RAM trim) via a Windows scheduled task - daily or
weekly - with a restore point beforehand and balloon notifications. A tray icon offers "run maintenance
now" and minimize-to-tray. Headless runs use `SystemCare.exe --run-maintenance`. Only one instance runs at
a time - launching again focuses the existing window.

### 🧾 Diagnostics
Rolling daily **logs** (maintenance, app updates, update checks, errors) under
`%AppData%\SystemCare\logs`, opened from **Settings > About > Open logs folder**; files older than 14 days
are pruned automatically.

## Download & install

1. Go to the [**latest release**](https://github.com/WifeInLantern/SystemCare/releases/latest).
2. Download **`SystemCare-Setup.exe`** under **Assets**.
3. Run it and accept the UAC prompt. It installs to Program Files with Start-menu (and optional desktop)
   shortcuts and an Add/Remove Programs entry. **No .NET install needed.**
4. Re-running a newer installer upgrades in place.

> **Heads-up:** the installer is **unsigned**, so Windows SmartScreen may warn on first run -
> choose **More info > Run anyway**.

Prefer no installer? The release also ships a standalone **`SystemCare.exe`** - just double-click it
(accept the UAC prompt; the app is `requireAdministrator`).

## Requirements

- **Windows 10 or 11 (x64)**
- **Administrator rights** - required for system temp, machine-level startup items, working-set trimming,
  DNS flush, service control, scheduled tasks and restore points.
- The **Software Updater** needs the Windows Package Manager (**App Installer / winget**); SystemCare shows
  guidance if it's missing.
- **Building from source:** the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Build from source

```powershell
# Requires the .NET 8 SDK. Run from an ELEVATED terminal - the app is requireAdministrator;
# launching from a non-elevated shell fails with error 740.
git clone https://github.com/WifeInLantern/SystemCare.git
cd SystemCare

dotnet build SystemCare.sln
dotnet run --project src\SystemCare\SystemCare.csproj
```

## Packaging

### Publish the single-file executable

```powershell
dotnet publish src\SystemCare\SystemCare.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

Output: `src\SystemCare\bin\Release\net8.0-windows\win-x64\publish\SystemCare.exe` (~73 MB). Copy it to
`dist\`. _WPF does not support trimming - do **not** add `PublishTrimmed`._

### Rebuild the installer

The installer is an [Inno Setup](https://jrsoftware.org/isinfo.php) script at
[installer/SystemCare.iss](installer/SystemCare.iss). After publishing `dist\SystemCare.exe`, recompile it:

```powershell
& "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe" installer\SystemCare.iss
# produces dist\SystemCare-Setup.exe
```

### Publish the installer checksum

The in-app updater verifies the SHA-256 of the installer it downloads when the release publishes one.
After building the installer, generate the checksum and attach **both** files to the GitHub release:

```powershell
(Get-FileHash dist\SystemCare-Setup.exe -Algorithm SHA256).Hash.ToLower() + " *SystemCare-Setup.exe" `
  | Out-File dist\SystemCare-Setup.exe.sha256 -Encoding ascii
# upload dist\SystemCare-Setup.exe AND dist\SystemCare-Setup.exe.sha256 to the release
```

Older releases without a `.sha256` still update (the updater falls back to verifying the byte count).

## Project layout

```
src/SystemCare/
  Models/       data records per module
  Services/     domain logic (scanning, cleaning, system info, updates) - no WPF types
  Native/       P/Invoke (GetSystemTimes, GlobalMemoryStatusEx, EmptyWorkingSet,
                SHQueryRecycleBin/SHEmptyRecycleBin, DnsFlushResolverCache, known folders)
  ViewModels/   MVVM view models (CommunityToolkit.Mvvm)
  Views/        one Page per nav item
  Controls/     HealthGauge, TreemapControl, SparklineChart, CyberBackground (custom-drawn)
  Helpers/      SafeFileEnumerator (reparse-point-safe walking), formatters,
                Animations (fade-in / smooth-value / hover-lift / neon-pulse / reveal behaviors)
  Styles/       Theme.xaml (neon palette tokens), Cyberpunk.xaml (WPF-UI token overrides,
                fonts, neon card + button styles)
  Assets/Fonts/ Orbitron + Rajdhani (OFL) bundled for the cyberpunk UI
installer/      Inno Setup script
dist/           release artifacts (SystemCare.exe, SystemCare-Setup.exe)
```

`bin/` and `obj/` are regenerable build outputs (git-ignored).

## Tech stack

| Area | Choice |
|------|--------|
| Runtime | .NET 8 (`net8.0-windows`), WPF |
| UI toolkit | [WPF-UI](https://github.com/lepoco/wpfui) 3.x (Fluent controls) |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Hardware sensors | [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) |
| System data | WMI (`System.Management`), registry, P/Invoke |
| Scheduling | [TaskScheduler](https://github.com/dahall/TaskScheduler) |
| Hashing | `System.IO.Hashing` (XxHash128) |
| App updates | winget (Windows Package Manager) |
| Installer | [Inno Setup](https://jrsoftware.org/isinfo.php) |

## Safety & privacy

- **Scans are dry-runs.** Cleaning only touches whitelisted locations; in-use and recently-modified files
  are skipped; junctions/symlinks are never followed.
- **Deletes go to the Recycle Bin** wherever possible (the File Shredder is the deliberate exception).
- **Reversible by default** - restore points before "Fix all", driver/software updates and disk
  maintenance; `.reg` backups before registry cleaning; reversible tweaks and boost.
- **Local-only.** SystemCare has no telemetry. Settings, history and logs stay on your PC under
  `%AppData%\SystemCare`. The only network calls are the optional update check (to your configured release
  feed) and the Software/Driver updaters (winget / Windows Update). The auto-updater only sends your
  optional GitHub token to GitHub hosts over HTTPS, and refuses to download an installer over plain HTTP.

## Auto-update & configuration

On startup (and from **Settings > Updates**) SystemCare checks a release feed for a newer version, then
downloads it and offers to install. Defaults target this repo's GitHub releases. Configurable via
`%AppData%\SystemCare\settings.json`:

- `UpdateFeedUrl` - a custom releases feed (GitHub `releases/latest` API URL by default).
- `UpdateGitHubToken` - optional token for a **private** release repo (only sent to GitHub over HTTPS).
- Plus auto-maintenance schedule, minimize-to-tray, restore-point safety, cleanup exclusions & custom
  folders, dashboard quick-actions, temp-age protection and large-file thresholds.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history. Recent highlights:

- **1.4.0** - refined Dashboard: smoother graphics and subtle new animations (glowing gauge tip + tick
  marks, flowing sparklines, counting CPU/RAM stats, gliding colour-shift drive bars, staggered entrances).
- **1.3.x** - reliable System Restore points (fixed the 24h-throttle silent no-op), settings/boost/updater
  bug fixes, and the new neon-shield logo across the app and splash.
- **1.2.0** - Software Updater (winget), diagnostic logging, security-aware health score, and updater
  hardening.

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). Anyone can fork the repo and open a pull
request (or file a bug/feature issue); **every change to `main` requires the maintainer's review and approval
before it can be merged.**

## License

SystemCare is **free to use** (personal and commercial) and **source-available**, under the terms in
[EULA.txt](EULA.txt) — which you accept during installation. You may not sell or rebrand it, and it is not an
OSI-approved open-source license (all rights reserved except as granted there). It runs locally with no
telemetry and is provided **as is, without warranty**.

## Disclaimer

SystemCare performs system-level maintenance (deleting files, editing the registry, changing services and
Windows settings). It ships with safety guards and reversible defaults, but **use it at your own risk** -
always keep backups and review what you're about to clean. The authors are not liable for any data loss or
system issues.

---

<div align="center">
Built with C# + WPF - themed for Night City 🌆
</div>
