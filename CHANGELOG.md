# Changelog

All notable changes to SystemCare are documented here. Versions follow [SemVer](https://semver.org/).

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

[1.4.2]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.4.2
[1.4.1]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.4.1
[1.4.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.4.0
[1.3.1]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.3.1
[1.3.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.3.0
[1.2.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.2.0
