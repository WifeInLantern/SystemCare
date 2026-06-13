# SystemCare

A Windows system-maintenance desktop app in the style of Advanced SystemCare, built with
C# + WPF on .NET 8 using the [WPF-UI](https://github.com/lepoco/wpfui) Fluent dark theme.

## Features

- **Dashboard** — animated PC health-score gauge plus live CPU, RAM and per-drive stats.
  One-click **Scan** and **Fix all** (clean junk + trim RAM, then re-score).
- **Junk Cleanup** — temp files, Windows Update cache, thumbnail cache, error reports,
  crash dumps, browser caches (Chrome/Edge/Firefox) and the Recycle Bin. Scan is always a
  dry-run; only whitelisted locations are touched; in-use files are skipped; recently-used
  temp files are protected; junctions/symlinks are never followed.
- **Startup Manager** — registry `Run` keys (incl. WOW6432Node), startup folders and
  scheduled tasks. Enable/disable uses the same `StartupApproved` mechanism as Task Manager,
  so entries are never deleted when you toggle them off.
- **Privacy Cleaner** — browser history/cookies/cache, recent-files & jump lists, Run-dialog
  history, recent documents, DNS cache flush and clipboard. Running browsers are detected and
  their locked data is skipped.
- **Disk Analyzer** — squarified treemap of a drive or folder with drill-down + breadcrumbs,
  and a top-N largest-files list. Delete sends files to the Recycle Bin.
- **Duplicate Finder** — size → partial-hash (first 64 KB) → full XxHash128 pipeline; keeps at
  least one copy per group; deletes to the Recycle Bin.
- **System Info & monitor** — CPU/GPU/motherboard/RAM/disk specs (via WMI) and live CPU, RAM
  and network sparkline graphs.
- **Software Uninstaller** — lists installed programs from the registry, runs each program's own
  uninstaller, and optionally recycles leftover folders.
- **Processes & Services** — live process list (RAM, CPU%, end task) and Windows services with
  start/stop and start-mode.
- **Disk Health & Maintenance** — SMART health per disk, plus CHKDSK / optimize / SFC / DISM with
  live streaming output.
- **Security Checkup** — Defender, Firewall, UAC, Remote Desktop and Windows Update status with
  quick-fix links.
- **Network Tools** — active connections per process (via the TCP table), ping/traceroute, and
  flush/renew DNS & IP.
- **One-click Boost** — switches to the High Performance power plan, frees RAM, and can pause
  selected background apps (reversible); one-click **Restore**.
- **Windows Tweaks** — reversible toggles for visual effects, telemetry, Explorer (show
  extensions/hidden, classic context menu), startup delay, and a power-plan switcher.
- **File Shredder** — securely overwrite (1–7 passes) and delete files/folders; irreversible.
- **Registry Cleaner** — conservative scan for orphaned entries (uninstall leftovers, App Paths, Run
  keys, shared DLLs, MUI cache) pointing to missing files. Always exports a `.reg` backup first
  (with **Restore last backup**) and can make a restore point — fully reversible.
- **Empty Folder Finder** — find and remove recursively-empty folders (to the Recycle Bin).
- **Deep Windows Cleanup** — reclaim big space: WinSxS component store (DISM), Windows.old,
  Delivery Optimization, Windows Update cache, and upgrade/setup leftovers, with a live console.
- **Bloatware & Store-app remover** — list and uninstall AppX/UWP apps; system-critical and
  framework packages are hidden so only safe apps are removable.
- **Rescue Center** — create/list System Restore points and open System Restore; a restore point
  is created automatically before "Fix all" and disk maintenance.
- **Automatic maintenance + system tray** — optional scheduled junk cleanup + RAM trim via a
  Windows scheduled task, a tray icon with "run maintenance now" and balloon notifications, and
  minimize-to-tray. Headless runs use `SystemCare.exe --run-maintenance`.
- **Update checker** — checks a configurable release feed (set `UpdateFeedUrl` in settings.json;
  compatible with a GitHub releases/latest URL) on startup and from Settings; notifies + offers a download link.
- **Dashboard quick-actions** — a customizable row of one-click tiles (Scan & Fix, Free RAM,
  Flush DNS, Empty Recycle Bin, Create restore point), toggled in Settings.
- **Settings** — light/dark theme, auto-maintenance schedule, minimize-to-tray, restore-point
  safety, cleanup exclusions & custom folders, dashboard quick-actions, update preferences,
  temp-age protection, large-file thresholds; persisted to `%AppData%\SystemCare\settings.json`.

The UI is a Fluent dashboard (dark by default, with a light theme) whose navigation is grouped
into **Clean / Optimize / Analyze / Protect** sections. Animated transitions include a glowing
health gauge, live sparkline graphs, count-up/gliding stats, card hover lift, staggered list
fade-ins, and an animated startup splash. Only one instance runs at a time — launching again
focuses the existing window.

The app requests administrator elevation (required for system temp, machine-level startup
items, working-set trimming, DNS flush, and service control). There is intentionally **no
registry cleaner**.

## Install / run the app

- **To give it to someone else:** send them **`dist\SystemCare-Setup.exe`** (~66 MB). They run
  it, accept the UAC prompt, and it installs SystemCare to Program Files with Start Menu (and
  optional Desktop) shortcuts and an entry in Add/Remove Programs. No .NET install needed.
- **To just run it yourself:** the standalone build is **`dist\SystemCare.exe`** (~71 MB, no
  dependencies) — double-click it (accept the UAC prompt; the app is `requireAdministrator`).

### Rebuild the installer

The installer is an [Inno Setup](https://jrsoftware.org/isinfo.php) script at
[installer/SystemCare.iss](installer/SystemCare.iss). After re-publishing `dist\SystemCare.exe`,
recompile it:

```powershell
& "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe" installer\SystemCare.iss
# → produces dist\SystemCare-Setup.exe
```

## Build & run from source

```powershell
# Requires the .NET 8 SDK. Run from an ELEVATED terminal (the app is requireAdministrator;
# launching it from a non-elevated shell fails with error 740).
dotnet build SystemCare.sln
dotnet run --project src\SystemCare\SystemCare.csproj
```

## Re-publish the single-file exe

```powershell
dotnet publish src\SystemCare\SystemCare.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

Output: `src\SystemCare\bin\Release\net8.0-windows\win-x64\publish\SystemCare.exe`; copy it to
`dist\`. Note: WPF does not support trimming, so do not add `PublishTrimmed`.

`bin/`, `obj/` and `dist/` are regenerable build outputs (git-ignored) — safe to delete.

## Project layout

```
src/SystemCare/
  Models/       data records per module
  Services/     domain logic (scanning, cleaning, system info) — no WPF types
  Native/       P/Invoke (GetSystemTimes, GlobalMemoryStatusEx, EmptyWorkingSet,
                SHQueryRecycleBin/SHEmptyRecycleBin, DnsFlushResolverCache, known folders)
  ViewModels/   MVVM view models (CommunityToolkit.Mvvm)
  Views/        one Page per nav item
  Controls/     HealthGauge, TreemapControl, SparklineChart (custom-drawn)
  Helpers/      SafeFileEnumerator (reparse-point-safe walking), formatters,
                Animations (fade-in / smooth-value attached behaviors)
  Styles/       Theme.xaml (palette tokens), Cards.xaml (hover lift + glow)
```
