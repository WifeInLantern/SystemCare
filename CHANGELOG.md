# Changelog

All notable changes to SystemCare are documented here. Versions follow [SemVer](https://semver.org/).

## [1.7.3] - 2026-06-18

### Changed
- **Redesigned System Information panel.** Hardware is now organized into clear, collapsible sections
  (Processor, Graphics, Memory, Storage, Operating system, Network) with a device/OS/uptime summary line
  and prioritized live CPU/RAM/Network monitors. The panel shows richer details — OS version, build and
  install date; RAM type and per-module info; GPU resolution and driver; disk SMART health; battery
  charge (laptops); per-adapter network type, link speed and IP; and live per-volume capacity bars — with
  health indicators and hover tooltips throughout.

### Fixed
- Detail lines no longer show stray "·" separators when a field is missing, and CPUs that under-report to
  WMI no longer read as "0 cores"/"0.0 GHz".
- Virtual and mirror display adapters (Basic Render, Remote Desktop, Parsec, DisplayLink) are no longer
  listed as graphics cards.
- The Memory row now shows an icon (the previous icon name didn't exist, so it rendered blank).
- Unsupported readings (battery, drive health, temperatures) are simply omitted instead of cluttering the
  panel, and a slow hardware scan can no longer freeze or crash the page.

### Performance
- The once-per-second usage snapshot (network and drive enumeration) now runs off the UI thread, and disk
  capacity refreshes on a slower cadence — removing micro-stutter on machines with many adapters or a VPN.

## [1.7.2] - 2026-06-18

### Security
- **The in-app updater now verifies what it downloads before running it.** Releases publish a SHA-256
  checksum for the installer; the updater downloads to a temporary file, verifies the byte count and
  (when published) the SHA-256, and only then promotes and launches it. A truncated download (e.g. a
  dropped connection) or a checksum mismatch is discarded instead of being launched with administrator
  rights. Releases without a checksum still update via byte-count verification.

### Added
- **A System Restore point is created before the app updates itself** (when "create a restore point"
  is enabled), giving you a rollback point before the installer replaces program files.

### Fixed
- **Declining the update's elevation prompt no longer closes the app.** Previously, starting an update
  from the startup prompt and then cancelling the UAC dialog could exit SystemCare without installing
  anything. The app now stays open if the installer doesn't actually start, and the downloaded installer
  remains in your Downloads folder to run later.

## [1.7.1] - 2026-06-18

### Fixed
- **Game Mode no longer changes your notification setting.** Exiting Game Mode used to force Windows
  notification toasts back *on*, overwriting the preference of anyone who keeps them off. It now records
  your toast setting when you enter and restores exactly that value (or removes it again if it wasn't set)
  on exit.
- **Game Mode / Boost toggle could show the wrong state.** Game Mode and Boost drive the same underlying
  engine; switching from one page to the other could leave a toggle showing "active" after the state had
  already been reverted. Each page now re-reads the real state when opened, so the toggle is always correct.
- **A single malformed Windows update no longer hides the rest.** If one pending update reported no title,
  the whole list could be cut short. Updates with a missing title now fall back to their KB number (or a
  generic label) and the full list is shown.

### Changed
- **Windows Update page hardening.** The initial load on opening the page is now fully guarded so a
  background failure can never bubble up; any problem is logged instead.

## [1.7.0] - 2026-06-18

### Added
- **Game / Focus mode.** A new Game Mode page (under Optimize) puts the PC into a low-distraction,
  high-performance state in one click and fully reverses it on exit. It switches to the High Performance
  power plan, suspends the background apps you select (browsers, chat, music, launchers are pre-checked),
  and trims standby RAM — and can optionally silence Windows notification toasts while active. Exit
  restores the power plan, resumes every app, and turns notifications back on. Built on the existing,
  suspend-count-safe Boost engine; each session is recorded in History.
- **Windows Update control.** A new Windows Update page (under Optimize) checks for and installs Windows
  (non-driver) updates without leaving the app: a checkbox list of pending updates, install with live
  progress and a reboot-required notice, a "Recently installed" history, and **Pause updates for
  7 / 14 / 35 days** or **Resume**. A System Restore point is created before installing, and there's a
  shortcut to the Windows Update settings page.
- **Boot & startup performance.** The Startup page gains a Boot performance card showing your last boot
  time, how long boot took, and current uptime, plus the slowest-starting apps and services color-coded
  by impact (green / yellow / red). Boot timing is read from the Windows Diagnostics-Performance event
  log; when that log isn't available it still shows last-boot time and uptime.

## [1.6.1] - 2026-06-17

### Added
- **Start with Windows.** A new Settings toggle launches SystemCare minimized to the tray when you sign
  in. Because the app requires administrator rights, it uses an elevated Task Scheduler logon task
  (highest privileges) instead of a Run-key entry, so it starts silently with no UAC prompt. The task is
  kept in sync with the setting and its path is refreshed on each launch (survives in-place upgrades).

## [1.6.0] - 2026-06-16

### Added
- **Windows Debloater.** A new Debloat page (under Protect) bundles curated, safety-vetted cleanups into
  one checkbox-driven workflow: disable telemetry & data collection (DiagTrack/dmwappushservice,
  `AllowTelemetry`, telemetry scheduled tasks), turn off ads/suggestions and the advertising ID, stop
  auto-installed "consumer feature" bloat, disable Cortana / Bing web search, optionally disable
  unneeded services (Xbox, Maps, Fax, WMP sharing), and optionally remove common preinstalled bloat apps.
  - **Safe by design:** acts only on a hardcoded allowlist (never arbitrary input); services are
    *disabled*, not deleted; a System Restore point is created first; most items have one-click **Revert**;
    a confirmation dialog reviews the selection (app removal is clearly flagged permanent). Every action is
    idempotent, logged, and recorded in History.

## [1.5.8] - 2026-06-16

### Added
- **Per-process bandwidth monitor.** The Network tab gains a Bandwidth panel showing live per-process
  download/upload speed, totals, and usage % (with app icon, PID, color-coded levels, speed bars, and
  high-usage highlighting), sorted by download/upload/combined. Uses an ETW kernel session (the same
  source Resource Monitor uses) and runs only while the Network page is open.

## [1.5.7] - 2026-06-16

### Changed
- **Loading indicator restyled** to a neon pill filled with blue-to-cyan vertical tick segments that
  light up left-to-right (with a glowing leading tick), and a live percentage at the end. Same honest
  timing - it climbs while a task runs and only reaches 100% when the task actually finishes.

## [1.5.6] - 2026-06-16

### Changed
- **Loading indicator restyled** to a horizontal green progress bar (lime fill with a darker leading
  block on a dark rounded track), replacing the vertical-bars look from 1.5.5. Same honest timing -
  it climbs while a task runs and only fills to 100% when the task actually finishes.

## [1.5.5] - 2026-06-16

### Changed
- **New task loading experience.** The old sideways indeterminate progress bar is replaced everywhere
  by a modern indicator: a row of vertical bars that fill left-to-right with a live percentage that
  climbs smoothly. The percentage is honest - it eases toward the high 90s while a task runs and only
  reaches 100% when the task actually finishes, then shows a success checkmark before fading. Where a
  real percentage is available (Software/Driver installs) the indicator tracks it directly. Lightweight
  and on-theme (one shared `TaskProgress` control across all 16 task screens).

## [1.5.0] - 2026-06-16

### Added
- **TreeSize-style Disk Analyzer.** The analyzer now shows an expandable folder tree so you can drill
  from a drive down to any folder or file and see exactly what is using space. Each row has an icon,
  name, a proportional size bar (colour-coded by file type), its size, and its share of the parent
  folder, sorted largest-first at every level. Right-click any row to open it in Explorer or move it
  (files or whole folders) to the Recycle Bin. The tree is built lazily and virtualized, so scanning a
  whole drive stays responsive. The "Largest files" panel is still there alongside it.

## [1.4.3] - 2026-06-16

### Fixed
- **No more hard-to-read dark text.** Many secondary labels (subtitles, card captions, list rows,
  status lines) were plain text with no colour set, so they fell back to a dark default against the
  dark background. Every page now sets a bright inherited text colour, so all text is readable while
  buttons keep their dark-on-accent text for contrast.
- **GPU VRAM detection hardened.** The 64-bit `HardwareInformation.qwMemorySize` reader now also
  understands the REG_BINARY form some drivers use, so a high-VRAM card can't fall back to the
  ~4 GB-capped value on those systems (the 1.4.1 fix only handled the REG_QWORD form).

## [1.4.2] - 2026-06-15

### Fixed
- **Page titles were hard to read.** Each page heading (e.g. "Junk Cleanup") was a plain `TextBlock`
  with no colour set, so it fell back to a dark default against the dark background. All page titles now
  use a shared bright heading style (`CyberPageTitle`).
- Reverted the 1.4.1 white accent-button text: dark text on the bright cyan/green buttons reads better
  than white. (The white-text request actually meant the dark page titles above.)

## [1.4.1] - 2026-06-15

### Fixed
- **GPU VRAM under-reported in System Info.** The GPU's memory was read from WMI's
  `Win32_VideoController.AdapterRAM`, a 32-bit value that caps at ~4 GB, so cards with more memory
  (e.g. a 12 GB GPU) showed 4 GB. It now reads the true 64-bit size from the adapter's registry
  `HardwareInformation.qwMemorySize`, falling back to the WMI value if unavailable.

### Changed
- Text on accent-coloured surfaces (Primary/Success buttons, selected items) is now **white** instead
  of near-black.

## [1.4.0] - 2026-06-15

### Changed
- **Refined Dashboard with smoother graphics and subtle new animations.**
  - **Health gauge**: glass gradient track, faint tick marks, a glowing dot at the leading tip of the
    score sweep, and a brief glow flash when a new score is computed.
  - **Live sparklines**: the CPU/RAM/network graphs are now flowing curves (Catmull-Rom smoothing)
    with a glowing dot tracking the latest value, instead of straight polylines.
  - **Live numbers**: the big CPU% and Memory readouts now count up/down smoothly instead of snapping
    each second.
  - **Drive cards**: usage bars glide to their value and shift colour (cyan -> yellow -> magenta) as a
    drive fills, with a hover lift.
  - **Entrances**: dashboard sections and tiles cascade in with a gentle staggered reveal.

## [1.3.1] - 2026-06-15

### Fixed
- **Startup splash showed the old logo.** The native splash image (`Assets/splash.png`, shown by the
  app host before the UI loads) still had the previous checkmark logo baked in, so the new neon-shield
  logo only appeared once the animated splash took over. The splash image now uses the new logo.

## [1.3.0] - 2026-06-15

A bug-fix and hardening release from a full audit of the services and view-models.

### Fixed
- **Restore points were silently not created.** Windows throttles restore-point creation to roughly
  once per 24 hours; while throttled the underlying call returns success but creates nothing, so the
  UI reported "Restore point created" without making one. SystemCare now clears that throttle before
  creating and verifies a new restore point actually appeared (its sequence number increased) before
  reporting success - otherwise it explains why (System Protection off / no disk space reserved).
- **Settings could be silently lost under concurrent saves.** The shared settings object is written
  from the UI, background scans and scheduled maintenance; those writes shared one temp file with no
  lock, so two at once could fail the atomic replace and drop a change. Saves are now serialized and
  use a unique temp file each time.
- **Boost could leave an app frozen.** Re-running Boost on an already-paused app suspended it twice,
  but a single Restore only resumed it once (Windows tracks a suspend count), leaving it stuck.
  Boost now skips processes it has already suspended, so Restore always brings them back.
- **Auto-updater hardening.** It no longer treats a non-installer release asset (a `.zip`, source
  archive or checksum file) as something to download and launch - only an `.exe` is ever run, and a
  GitHub release with no installer is correctly reported as "no installer attached".

### Security
- The updater only sends the optional GitHub token to GitHub hosts over HTTPS, and refuses to
  download the installer over plain HTTP (carried over from 1.2.0 hardening).

## [1.2.0] - 2026-06-15

### Added
- **Software Updater (winget)** - update installed apps, ignore specific apps, or update all at once,
  with a restore point first.
- **Diagnostic logging** - rolling daily logs under `%AppData%\SystemCare\logs`, opened from
  Settings > About.
- **Security-aware PC Health score** - the dashboard score now factors in Defender, firewall, UAC,
  Remote Desktop and Windows Update status.
- **Automatic scheduled maintenance**, **per-component temperatures**, **Activity History**, and a
  **built-in GitHub updater**.

### Security
- Scoped the updater's GitHub token to GitHub HTTPS hosts and enforced HTTPS-only installer downloads.

## [1.1.0]
- Uninstaller leftover scanning, per-component temperatures, activity history, software updater groundwork.

## [1.0.0]
- Initial release: cleanup, privacy, duplicates, disk tools, startup, boost, tweaks, security, and more.

[1.7.3]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.7.3
[1.7.2]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.7.2
[1.7.1]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.7.1
[1.7.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.7.0
[1.6.1]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.6.1
[1.6.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.6.0
[1.5.8]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.5.8
[1.5.7]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.5.7
[1.5.6]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.5.6
[1.5.5]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.5.5
[1.5.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.5.0
[1.4.3]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.4.3
[1.4.2]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.4.2
[1.4.1]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.4.1
[1.4.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.4.0
[1.3.1]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.3.1
[1.3.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.3.0
[1.2.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.2.0
