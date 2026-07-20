# Changelog

All notable changes to SystemCare are documented here. Versions follow [SemVer](https://semver.org/).

## [2.19.2] - 2026-07-20

### Fixed
- **CPU temperature no longer sticks at an impossible ~17 °C.** When the real sensor is blocked
  (Memory Integrity) the app falls back to the ACPI thermal zone — but many boards expose a frozen
  placeholder there (290 K) that never updates. A running CPU can't sit below room temperature, so
  implausible readings (≤20 °C) are now rejected and the Sensors hub shows its honest
  "CPU temperature unavailable" explanation instead of a false number.

## [2.19.1] - 2026-07-20

### Fixed
- **Sensors no longer show "0 MHz" rows.** Parked CPU cores report zero "effective" clocks, and a
  blocked sensor driver (Windows Memory Integrity/HVCI) can zero out whole clock and voltage banks —
  those rows are noise, not signal, and are now dropped from the Sensors hub. Zeros that carry
  meaning (a stopped fan, 0% load) still show.

## [2.19.0] - 2026-07-20

### Added
- **Explainable health score.** Under the Dashboard gauge, clickable chips now show the score's
  actual inputs — junk found, startup items, RAM load, security posture — each jumping straight to
  its tool, and "Fix all" states exactly what it will do in its tooltip. The score stops being a
  black box.
- **Restore-point watchdog.** The whole app leans on System Restore as its safety net; now it's
  monitored. A tray notice (at most once per 14 days) warns when no restore point exists from the
  last 30 days — which also catches System Protection being switched off.
- **Autorun Guard re-checks every 6 hours** while the app runs — programs that add themselves to
  startup mid-session no longer go unnoticed until the next launch.
- **Design-system lint in CI.** The smoke test now fails the build if any text element in Views
  carries an inline `Opacity` (the WCAG-breaking anti-pattern), and reports the remaining inline
  `FontSize` count as a migration metric. The cleanup below can never silently regress.

### Changed
- **Text-contrast cleanup completed app-wide.** The remaining 159 instances of opacity-dimmed text
  across 33 pages were replaced with AA-compliant color tokens (size and layout untouched). Every
  piece of text in the app now meets WCAG AA contrast.

## [2.18.0] - 2026-07-20

### Changed
- **UI refinement: ten more pages moved onto the design-system type tokens.** Speed Test, Scheduled
  Tasks, Context Menu, Boot Analyzer, Privacy, Cleanup, Startup, Boost, Game Booster, and Sensors
  no longer stack inline `Opacity` on small text (~34 instances) — every affected string now sits
  on `TextBody`/`TextCaption` or an AA-compliant color token. Rows with `TextTrimming` kept their
  exact size/layout and only had their contrast fixed, so nothing reflows.

### Fixed
- **Ransomware Shield page could pop a raw error dialog** if the Defender/WMI status read failed
  during page load — the only remaining unguarded page load; it now degrades to a clear headline
  like every other page.
- **Extension Audit could read a stale manifest.** Chromium version folders were picked
  alphabetically, so "9.0" beat "10.0"; the newest folder (by write time) now wins.

## [2.17.0] - 2026-07-14

### Added
- **Browser Extension Audit** (new tool under Protect). Lists every extension across Chrome, Edge,
  Brave (all profiles) and Firefox by reading the on-disk manifests, and classifies each by
  permission reach — extensions that can "read data on all websites" combined with sensitive APIs
  (webRequest, cookies, history, clipboard…) are flagged **High reach**. Strictly read-only.
- **Wi-Fi Analyzer** (new tool under Analyze). Current connection's signal, channel, band and link
  rates, every network in range, and a channel-congestion hint ("7 networks share channel 6").
  Parses `netsh wlan`; degrades gracefully on systems without wireless.
- **CLI: `--report [path]`** exports the Care Report headlessly (defaults to
  `Documents\SystemCare\SystemCare-report-<date>.html`) — pairs with the 2.14 maintenance verbs
  for scripted fleets.

### Fixed
- **Temperature alerts could freeze the UI.** The 2.16 sensor read ran on the metrics event, which
  is raised by a UI-thread DispatcherTimer — LibreHardwareMonitor reads (100 ms+) now run on the
  thread pool with a reentrancy guard, resuming on the UI context only to raise the alert.
- **Search-index rebuild could hang forever** if the Windows Search service was slow to stop; the
  service restart now has a 90-second timeout with an honest message (the rebuild flag persists, so
  a reboot completes it).
- **A failing tray action could crash the app.** The tray menu's maintenance handlers are
  async-void on the tray thread; exceptions there tore the process down. All of them (including
  the pre-existing "Run maintenance now") now contain failures and report them as a balloon.
- **Ad Blocker buttons could double-fire.** The 2.14 "Update from StevenBlack" / "Use built-in
  list" buttons didn't disable while busy, so a second click could start a concurrent download.

## [2.16.0] - 2026-07-11

### Added
- **Temperature alerts.** The resource-alert engine now optionally watches CPU and GPU temperature
  (sampled every ~30 s via the existing hardware-sensor backend) and raises the same sustained-breach
  toast + tray notification when either stays at or above a configurable threshold (default 90 °C).
- **Boot report.** SystemCare keeps a per-boot duration history and, when a start-up is ≥25% slower
  than your recent median (with at least 5 boots of history), tells you once via a tray notification —
  with Boot Analyzer and Startup Manager as the follow-up. No noise on one-off slow boots.
- **Hibernation & Pagefile advisor** (new tool under Optimize). Shows what `hiberfil.sys` and
  `pagefile.sys` actually reserve, explains the trade-offs in plain language, and offers the
  reversible `powercfg` actions: disable, re-enable, or **reduced** mode (keeps Fast Startup at
  ~40% of RAM). The page file itself is deliberately read-only — informing without foot-guns.
- **Accent color picker.** Settings → Appearance now offers Cyan (default), Magenta, or Violet for
  accent-driven controls (buttons, toggles, selection), applied live. The neon identity — glows,
  charts, headers — keeps its design-system colors.
- **Tray quick actions.** The tray menu gains a Quick actions submenu: Clean junk, Free up RAM,
  Flush DNS cache, Empty Recycle Bin — each runs through the maintenance pipeline with a result
  balloon, no window needed.
- **Monthly care report.** Optional: once a month, the Care Report is exported as HTML to
  `Documents\SystemCare` and announced with a tray notification.
- **Windows Search index health** (in Repair Toolkit). Shows the size of the search database
  (`Windows.edb`/`Windows.db` — a classic hidden disk hog), opens Indexing Options, and can trigger
  the documented rebuild (the same flag Indexing Options' own Rebuild button sets, then a WSearch
  restart).

### Notes
- Temperature alerts sit behind the existing "Resource alerts" master switch and share its
  sustained-minutes window.
- The boot report needs a few boots of history before it can say anything.

## [2.14.0] - 2026-07-11

### Added
- **Autorun Guard.** SystemCare now snapshots your startup entries and, on the next launch, notifies
  you when a program has silently added itself to startup — with a pointer to the Startup Manager,
  where the existing reversible toggle can disable it. Detection only; nothing is changed
  automatically. On by default; toggle in Settings.
- **App Cache Cleaner** (new tool under Clean). A curated, whitelisted catalog of regenerable caches:
  Discord, Slack, classic Teams, Spotify, NVIDIA/Steam shader caches, plus a developer group
  (npm, Yarn, pip, NuGet HTTP cache, Gradle). Dry-run scan first; files written in the last 24 hours
  and in-use files are always left alone; the NuGet global packages folder is never touched.
- **Storage Forecast.** Dashboard drive cards now warn "~5 weeks until full at the current rate"
  when a drive shows a sustained downward free-space trend (least-squares fit over daily samples;
  it stays silent without a meaningful trend). Unit-tested math.
- **Crash correlation in Reliability.** Below the recent-issues list, a "What changed beforehand"
  panel lists what SystemCare itself changed (driver installs, updates, tweaks…) in the 72 hours
  before the most recent serious event — the fastest first lead when instability starts, with a
  pointer to Rescue Center for rollback. Correlation, not causation, and labelled as such.
- **Community blocklist for the Ad Blocker.** One click downloads the StevenBlack hosts list
  (tens of thousands of domains, capped at 60k) and uses it instead of the small built-in list,
  with a validity check before anything is applied and a one-click return to the built-in list.
- **Idle-aware automatic maintenance.** New Settings toggle: run the scheduled pass only while the
  PC is idle and on AC power (Task Scheduler idle trigger).
- **CLI verbs for scripts.** `SystemCare.exe --clean --trim-ram --flush-dns --empty-recycle-bin`
  runs a headless ad-hoc maintenance pass with exactly the steps you pass (the scheduled
  `--run-maintenance` keeps using your Settings profile).
- **Exclusions Center groundwork.** The Software Updater's ignore list is now reviewable and
  editable in Settings alongside the cleaner's excluded/custom folders — every exclusion in one place.

### Notes
- Autorun Guard's first run only establishes a baseline; alerts begin from the second launch.
- The community blocklist is fetched from the StevenBlack GitHub repository; if the download looks
  wrong (too few entries) the current list is kept.

## [2.13.0] - 2026-07-11

### Added
- **Visible tool search.** A "Search" entry now sits at the top of the navigation pane and opens the
  Ctrl+K command palette — previously the palette was reachable only by knowing the hotkey. The entry
  is keyboard-activatable (Enter/Space) and carries a screen-reader name.
- **Design System v6 foundation** ("Night City, Refined" — see `docs/UI-REDESIGN-V5.md`): new
  `TextQuaternaryBrush`, `Space3Xl`, and `ContentMaxWidth` tokens, and a `TextMetricHero` style for
  hero numbers. All are covered by the SmokeTest CI gate.

### Changed
- **Wider, more legible type ramp.** `TextH2` 15→17, `TextH3` 13→14, `TextBody` 13→14 (+ line
  height), `TextBodyStrong`/`TextMetricValue` 13→14, `TextCaption` 11→12 (+ line height). The old
  4px spread was so compressed that pages kept inventing inline sizes.
- **Every tool now has a unique nav icon.** Browser Cleanup, Network, and Secure DNS no longer share
  one globe (now `GlobeClock24` / `Router24` / `LockShield24`); Debloat no longer duplicates
  Cleanup's broom (`BoxDismiss24`); Breach Checker and Security Audit got distinct shields
  (`Password24` / `ShieldGlobe24`).
- **Dashboard, Large Files, and Secure DNS retrofitted to the type tokens.** Inline
  `FontSize`/`Opacity` overrides replaced with ramp styles — text on these pages now meets
  WCAG AA contrast (stacked opacity had been dropping captions as low as 3.1:1).

### Fixed
- **Disabled text was unreadable** at 2.1:1 contrast; raised to ~3.6:1 (`#5A6E8C`) while still
  clearly reading as inactive.
- **Large Files columns no longer clip.** The fixed 110px size/age columns are now auto-sized with
  a shared size scope, so long byte-strings and dates survive narrow windows and high DPI.

## [2.12.1] - 2026-07-10

### Fixed
- **Hardware Info could silently go blank.** The WMI query backing CPU/GPU/disk model lookups returned its
  result collection after disposing the searcher that owned it — undefined behavior per WMI's own contract.
  Results are now fully read out before the searcher is released.
- **File Shredder, Empty Folder Finder, and Duplicate Finder could be double-triggered.** Their destructive
  action buttons stayed clickable while already running, so a second click mid-operation could corrupt shared
  cancellation state — in the shredder's case, silently breaking Cancel and risking an exception mid-pass on
  an irreversible delete. These buttons now disable for the duration of the operation, matching every other
  long-running action in the app.
- **A stray `TaskProgress` control could leak.** It was missing the unsubscribe-before-subscribe guard its
  sibling controls already use for the shared Reduce-motion event, so a `Loaded`/`Unloaded` re-fire could
  double-subscribe it and keep a finished control alive (and reacting to settings changes) indefinitely.
- **A failed page load could pop a raw technical error dialog.** 19 pages had no `catch` around their initial
  load, so a transient failure (e.g., a WMI or registry read throwing) surfaced as an unstyled exception
  message box instead of the page just failing quietly, as the rest of the app already does.

## [2.12.0] - 2026-07-08

### Changed
- **Safer bloatware detection.** App-name matching is now anchored to the package's family segments
  instead of a loose substring search over the whole identifier, removing the risk of a keyword
  matching across a name boundary or inside the publisher hash and flagging an app it shouldn't.
- **Bloatware removal now logs failures.** Package-listing and removal operations that previously failed
  silently now record a diagnostic entry (and no longer risk reporting success when nothing was removed),
  so problems show up in the log under `%AppData%\SystemCare\logs`.

### Fixed
- **Removal progress updates are marshalled to the UI thread** via `IProgress<T>`, instead of writing
  bound UI state directly from the background worker.

### Build
- **Optional code signing.** The release pipeline now signs `SystemCare.exe` and the installer with
  Authenticode when a signing certificate is configured (`SYSTEMCARE_PFX` / `SYSTEMCARE_PFX_PASSWORD`),
  and skips signing cleanly when none is present.

## [2.11.0] - 2026-07-08

### Performance
- **Faster bloatware removal.** Removing several apps at once now runs in a **single** background process
  instead of launching one `powershell.exe` per app, cutting per-app startup overhead on large cleanups
  while still reporting progress app-by-app.
- **Faster cold start.** The published app is now precompiled to native code (ReadyToRun) and its single-file
  bundle is no longer compressed, so it launches noticeably quicker (at the cost of a larger on-disk exe; the
  installer download stays compressed).
- **Lower idle CPU.** The animated cyberpunk backdrop now pauses whenever the window is unfocused or minimized,
  and resumes instantly when you return — no frames are drawn while you can't see them.
- **Fewer process spawns.** Ransomware-protection status is now read directly from Windows Defender via WMI
  (was two PowerShell launches per refresh), and the ad/tracker blocklist flushes the DNS cache with an
  in-process system call instead of launching `ipconfig`.

## [2.10.0] - 2026-07-08

### Changed
- **Revamped the PC Health score.** The 0–100 gauge now uses a rebalanced, weighted model (v2) that reflects
  real impact rather than mere tidiness: **security posture** (up to 30) and **low system-drive free space**
  (up to 25, a new factor) carry the most weight; **sustained memory pressure** (up to 20) counts only past
  70% load since Windows uses free RAM for caching; and **startup load** (15) and **junk** (10) are now gentle,
  so ordinary housekeeping no longer drags an otherwise-healthy PC down. Auto Care's recommendations were
  re-anchored to the new weights.
- **Removed the Quick Actions row from the Dashboard** for a cleaner hero area. Those actions remain available
  from their own pages, and the hero's Scan / Fix all / Free up RAM buttons are unchanged.

## [2.9.0] - 2026-07-08

### Changed
- **Bloatware removal now works like Win11Debloat.** Removing a flagged app uninstalls it **for all users**
  and also removes the **provisioned package**, so it no longer reinstalls for new user accounts or comes back
  after removal. The page now enumerates apps installed for all users, and success is verified (the app is
  re-checked after removal).
- **Bloatware list aligned with Win11Debloat's curated catalog.** The flagged set was expanded to cover the
  Bing suite, Copilot, Dev Home, the new Outlook & Teams, Xbox overlays, OEM apps (HP/Dell/Lenovo), and common
  third-party preinstalls (King games, Spotify, Disney+, Netflix, TikTok, LinkedIn, etc.). Genuinely useful
  apps (Calculator, Camera, Photos, Notepad, classic Paint, Snipping Tool, Terminal) and game-required Xbox
  components (TCUI, Identity Provider, Speech-to-Text) are deliberately left unflagged.

## [2.8.1] - 2026-07-07

### Changed
- **Responsive layout polish** (from the UI/UX audit). Action toolbars on Browser Cleanup, Ransomware Shield,
  and the Ad/Tracker Blocker now wrap to a second line instead of clipping when the window is made narrow, and
  the Secure DNS adapter selector resizes fluidly instead of using a fixed width. Desktop window-resize
  resilience only — no functional change.

## [2.8.0] - 2026-07-07

### Added
- **Command palette (Ctrl+K).** Press Ctrl+K anywhere to open a search overlay and jump to any of the app's
  tools by name — no more scrolling the sidebar. Type to filter, Up/Down to move, Enter to open, Esc (or a
  click outside) to close. The list is built from the live navigation, so it always stays in sync, and it
  honours the Reduce-motion setting. This is the navigation-at-scale improvement from the UI/UX audit for an
  app that now has ~50 tools.

## [2.7.0] - 2026-07-07

### Changed
- **Design-system compliance & accessibility pass (v5, additive).** Following a full UI/UX audit, the design
  system gained a few missing tokens — `TextBodyStrong`, `TextMetricValue` (right-aligned numerals),
  `CyberChipNeutral`/`ChipTextNeutral` — and an accessible keyboard-focus ring (`CyberFocusVisual`), now
  applied to interactive cards so keyboard users get a visible focus indicator. Several pages that had drifted
  off the system by inlining a `#33…` chip colour literal (Security, Breach Checker, Scheduled Tasks, Context
  Menu) now use the neutral surface token instead. Purely visual-parity + accessibility; no functional change.
  All new keys are verified by the design-system smoke test in CI.

## [2.6.1] - 2026-07-07

### Fixed
- **Speed Test now reports partial results.** Previously, if the upload leg failed (e.g. the upload host was
  briefly unreachable), a perfectly good download + ping result was discarded and the whole test showed
  "failed." Each measurement now runs independently, so you still see what succeeded, with a note about any
  leg that was unavailable.

### Internal
- Added unit tests for the Game Booster rollback engine (apply/journal/revert round-trip, reverse-order
  revert, Safe/Advanced tier gating, best-effort revert, and interrupted-session recovery), locking in the
  crash-safe rollback contract introduced in 2.6.0.

## [2.6.0] - 2026-07-06

### Changed
- **Game Mode is now Game Booster.** The feature is renamed throughout, and its internals are rebuilt on a
  new **reversible-optimization engine**: every change (High Performance power plan, background-app suspend,
  RAM trim, notification silence) captures its prior state and is written to a durable session journal at
  `%AppData%\SystemCare\gamebooster\session.json`.
- **Crash-safe rollback.** If the app crashes or the PC loses power mid-session, the next launch detects the
  interrupted session and restores every change automatically — the previous release could only roll back
  in-memory. Behaviour is otherwise identical to Game Mode; this is the foundation for upcoming Game Booster
  layers (service pausing, scheduled-task deferral, CPU/GPU/network tuning, and automatic game detection).

## [2.5.5] - 2026-07-06

### Added
Ten new tools across every section:

- **Secure DNS (Protect).** Switch your adapter's DNS to Cloudflare, Google, Quad9, or OpenDNS in one
  click — or revert to automatic (DHCP). Applied via `netsh`.
- **Ransomware Shield (Protect).** Turn Windows Defender's Controlled Folder Access on/off and see your
  protected folders.
- **Ad / Tracker Blocker (Protect).** Apply a curated ad/tracker blocklist to the hosts file
  system-wide, with a one-time backup of your original hosts file and clean removal.
- **Breach Checker (Protect).** Check whether a password appears in a known breach via Have I Been
  Pwned's k-anonymity API — only the first 5 characters of the hash ever leave your PC.
- **Large & Old Files (Clean).** Scan any folder for the biggest files, see when each was last opened,
  and send the ones you don't need to the Recycle Bin.
- **Browser Cleanup (Clean).** Detect installed browsers (Chrome, Edge, Brave, Firefox) and clear
  cache, cookies, and history per-browser.
- **Scheduled Tasks (Optimize).** List and enable/disable third-party Windows scheduled tasks
  (updaters, telemetry, helpers).
- **Context-Menu Manager (Optimize).** Declutter the right-click menu by toggling shell extensions on
  or off — fully reversible.
- **Boot-Time Analyzer (Analyze).** See your boot duration, uptime, and the apps/services that slow
  startup the most.
- **Network Speed Test (Analyze).** Measure download, upload, and ping against Cloudflare's network.

## [2.4.5] - 2026-07-05

### Fixed
- **Hardening for the Defender & Battery Health tools** (from a code review of 2.4.3):
  - Battery WMI queries now dispose their searcher, result collection, and result objects correctly,
    and read every field defensively — a device that doesn't expose a property (e.g. `DeviceName`)
    no longer aborts the remaining reads.
  - Defender's **Quick scan / Full scan / Update definitions** actions are now disabled when Defender
    isn't the active antivirus, instead of failing after the click.
  - The newest Defender platform build is selected by parsed version rather than lexical order.
  - A never-run scan now shows "Never" instead of an epoch date.

### Internal
- Added unit tests for battery wear/health math and Defender status mapping.

## [2.4.4] - 2026-07-05

### Changed
- **Software Hub: a much bigger, Ninite-style catalog.** The one-click installer grew from 20 to 58
  curated free apps, organized into 12 categories (Web Browsers, Messaging, Media, Imaging,
  Documents, Developer Tools, Utilities, Compression, Security, Online Storage, Runtimes, File
  Sharing). Every winget package id is validated against the live winget catalog by a new
  `tools/verify-softwarehub-ids` checker.

### Fixed
- **Brave's install id was stale.** The Software Hub used the retired `BraveSoftware.BraveBrowser`
  id, so installing Brave silently failed; it now uses the current `Brave.Brave`.

## [2.4.3] - 2026-07-05

### Added
- **Defender page (Protect).** A dedicated Microsoft Defender panel that reads live protection status
  via WMI — real-time protection, tamper protection, definition version/age, and the last quick/full
  scan times — and runs **Quick scan**, **Full scan**, or a **definition update** through
  `MpCmdRun.exe` with streaming console output and cancel support. Results are recorded to Activity
  History, and a shortcut opens Windows Security for details.
- **Battery Health page (Analyze).** A laptop battery report that computes **wear level** from design
  vs. full-charge capacity (via WMI), shown on the neon health gauge alongside cycle count, current
  charge, chemistry, and power state. An **Export detailed report** button generates Windows'
  `powercfg /batteryreport` HTML and opens it. Desktops without a battery get a clean empty state.

## [2.4.2] - 2026-07-03

### Changed
- **UI polish: Design System v4.** A follow-up pass on top of 2.4.1's overhaul — every neon glow
  (section headers, hover states, hero elements) is pushed noticeably stronger, dialogs and flyouts
  read with more contrast against the backdrop, and hover cards now fade their glow in and out
  consistently everywhere (the one remaining spot that still popped instantly is fixed). Page
  entrances and reveals run a touch longer for a smoother feel, and a couple of hero elements (the
  health gauge, Auto Care's primary button) get a subtle spring overshoot on entrance. Live stat
  numbers on the Benchmark, Reliability, Sensors, and Care Report pages now count up smoothly
  instead of snapping to their final value, matching the Dashboard. The Cleanup page shows a
  shimmering placeholder while scanning instead of blank rows, and a couple of pages adopt the
  glass-panel and status-chip styles introduced last release. Visual/interaction refresh only — no
  functional changes.

## [2.4.1] - 2026-07-02

### Changed
- **UI overhaul: Design System v3.** The cyberpunk neon theme is refined with layered glassmorphism
  surfaces (a new elevation scale with rim-light strokes), translucent status chips, and a much
  broader motion pass — every page now has a staggered entrance, hover glows fade in/out instead of
  popping, and buttons get a tactile press/spring animation. A new `CyberPalette` helper means
  palette edits now propagate to the custom-drawn controls (health gauge, backdrop, task progress),
  which previously hardcoded their own colors. The "Reduce motion" setting was extended to cover
  every new effect, including a control (task progress) that previously ignored it, and the nav
  transition now respects it live. Visual/interaction refresh only — no functional changes. A new
  `docs/DESIGN-SYSTEM.md` documents the tokens, motion spec, and component patterns for future work.

## [2.4.0] - 2026-07-02

### Added
- **Auto Care.** A new page right under the Dashboard that analyzes junk, startup load, RAM pressure,
  security posture, and pending app updates in one click, then lists ranked, explained recommendations.
  Each card shows why it matters and how many health points the fix recovers (anchored to the real
  health-score penalty model); direct fixes (junk clean, RAM trim) apply in place with the usual
  restore-point safety net, and review items deep-link to the matching tool.
- **Net Monitor.** Per-app bandwidth monitoring is now its own Analyze page with a live total-throughput
  graph, session download/upload totals, and per-process rates with sortable columns. (Moved out of
  Network Tools, which now focuses on connections and diagnostics; a link card points the way.)
- **Software Hub search.** Search the entire winget catalog from the Software Hub — results show
  already-installed apps and install with one click via the same restore-point-gated flow. The curated
  catalog remains the default view; searches are debounced and stale results are cancelled.
- **Care Report & Trends.** A new Analyze page charting space freed per day and per week, actions by
  category, and health-score + benchmark trends from your local activity history — plus a one-click
  export to a self-contained dark-themed HTML report (system specs, health, activity summary; no
  scripts, nothing leaves your PC). Health scores are now snapshotted once per day to build the trend.
- **Configurable scheduled maintenance.** Settings now lets you choose exactly what the scheduled
  `--run-maintenance` pass (and the tray's "Run maintenance now") does: clean junk, trim RAM, flush
  DNS, and/or empty the Recycle Bin. Junk + RAM stay the defaults; each step is fault-isolated so one
  failure no longer aborts the rest, and completion balloons report exactly what ran.
- **Benchmark run details.** An expandable "Run details" section on the Benchmark page lists every
  stored run's raw CPU/RAM/disk throughput, points, and sub-scores.

## [2.3.7] - 2026-07-02

### Added
- **Software Hub.** A new **Optimize → Software Hub** page lists a curated catalog of ~20 popular free
  apps (browsers, 7-Zip, VLC, Git, Discord, and more) grouped by category, installable via the Windows
  Package Manager (winget) with a single "Install selected" action. Already-installed apps are detected
  from `winget list` and shown with an "Installed" badge with their checkbox disabled, so you can't
  accidentally re-install something you already have. Installing is gated behind the same restore-point
  confirmation used by the Software Updater.

## [2.3.6] - 2026-07-01

### Fixed
- **Software Uninstaller no longer crashes when leftovers are found.** After uninstalling an app that left
  files behind, the "leftovers found" review dialog threw an *Unexpected error* (`Provide value on
  'System.Windows.Baml2006.TypeConverterMarkupExtension' threw an exception`) and never appeared, so leftovers
  couldn't be reviewed or removed. The dialog's list used `SelectionMode="None"`, which isn't a valid value
  for WPF's `SelectionMode` enum (only `Single`/`Multiple`/`Extended`); it now uses `Single`. A headless
  design-system smoke-test check now instantiates this dialog so an XAML-load regression like this fails CI.

## [2.3.5] - 2026-07-01

### Fixed
- **Software Uninstaller now finds leftovers for short-named apps.** The leftover scanner requires a
  "distinctive" brand token before flagging a folder, but that token had to be 4+ characters — so apps whose
  brand is a short acronym (VLC, Git, OBS) produced no distinctive token and their leftovers were silently
  missed. A single 3-character, non-generic acronym is now treated as distinctive. Names of 1–2 characters
  ("Go", "R") and generic filesystem acronyms ("bin", "log", "tmp"…) are still excluded, so the
  false-positive protection is unchanged.
- **Software Uninstaller now reports the real uninstall outcome.** `UninstallAsync` previously always
  reported success once the uninstaller launched. It now reflects the uninstaller's exit code (treating 0 and
  the MSI reboot-required/initiated codes as success). A cancelled or failed uninstall is reported as such and
  **skips the leftover scan**, so SystemCare never offers to delete a still-installed app's live files.

## [2.3.4] - 2026-07-01

### Changed
- Hardened the **Software Uninstaller**'s leftover-removal safety with comprehensive unit tests. They lock
  in the conservative token matching (a distinctive 4+ character, non-generic token is required, so removing
  e.g. "Media Player" can never flag an unrelated `Media` folder), the `AcceptFolder` guards that reject
  drive roots, protected system roots (Windows, Program Files, the AppData/profile roots), and folders shared
  with other installed apps, and that removal routes files to the Recycle Bin and registry keys through the
  backed-up registry-clean pipeline. No behavioral change — this release only adds test coverage proving the
  existing leftover removal is safe and correct.

## [2.3.3] - 2026-07-01

### Fixed
- **Software Updater now detects updates again.** The Optimize → Software Updater page could silently report
  "All your apps are up to date" even when updates were available. winget renders its progress spinner using
  carriage returns on the same line as the results-table header; the parser stripped those carriage returns,
  which fused the spinner text in front of the column headers and pushed every column offset past the end of
  the data rows, so nothing was parsed. The parser now normalises carriage returns to newlines (keeping the
  header aligned), strips a stray BOM, and logs a warning if winget returns output that yields no rows so the
  failure is no longer silent. Verified against winget 1.28 with eight pending updates.

### Changed
- winget process launching and discovery were extracted behind an `IWingetRunner` abstraction, and the
  upgrade-table parser into `WingetUpgradeParser`, so the Software Updater is now covered by unit tests
  (parser, service, and view-model) using captured real-world winget output.

## [2.3.2] - 2026-06-30

### Added
- **Network Security Audit.** A new **Analyze → Network Security** page lists every process holding an open
  TCP or UDP socket, and lets you block any app from the network with one click via a Windows Firewall rule.
  SystemCare-created block rules are listed separately so they can be removed just as easily. No traffic is
  inspected; it only adds or removes named firewall rules for the selected executable.
- **System Repair Toolkit.** A new **Protect → Repair Toolkit** page provides guided SFC, DISM, and
  CHKDSK repair — individually or as a sequenced "run all three" pass. Each step streams live output to
  a console, is cancellable, and is gated behind a restore-point prompt. Plain-language result summaries
  are shown on completion (CHKDSK on the system volume reports "scheduled for next restart" rather than
  silently deferring).
- **Proactive Resource Alerts.** SystemCare now watches CPU, RAM, and disk-space usage in the background
  and fires a tray notification + snackbar once per sustained breach (configurable threshold and duration,
  defaults: 90 % CPU/RAM and 95 % disk for 5 minutes). Controlled via a new **Settings → Resource alerts**
  card with per-metric threshold and duration sliders. Alerts reset automatically when the metric recovers.

### Fixed
- Improved test coverage and reliability across all ViewModel and service tests.

## [2.3.1] - 2026-06-29

### Added
- **Installation agreement.** The installer now presents a license agreement (EULA) that must be accepted
  before installing — free, source-available terms with the standard no-warranty / use-at-your-own-risk and
  "runs as administrator and changes system settings" notice. The project also gained contributor guidance
  (CONTRIBUTING, issue/PR templates); contributions are welcome and reviewed by the maintainer.

## [2.3.0] - 2026-06-29

### Added
- **Sensors & Thermals hub.** A new **Analyze → Sensors** page consolidates live hardware monitoring into one
  cyberpunk dashboard: temperatures, fan speeds, voltages, clocks, per-component load and power, grouped by
  device, with live CPU/GPU temperature and load graphs. Hot sensors are highlighted and a tray notification
  fires once when a component crosses a critical temperature. Polls only while the page is open; fully local.
- **Reliability Center.** A new **Protect → Reliability** page reads the Windows Event Log to surface recent
  blue screens, unexpected shutdowns, app crashes/hangs, disk errors and service failures from the last 14
  days, rolled into a 0–100 stability score with per-category counts and a recent-issues list. Includes
  one-tap fixes — run system repair, create a restore point, open the Rescue Center, or open Event Viewer.
  Reads the local log only; nothing leaves your PC.

## [2.2.0] - 2026-06-28

### Added
- **PC Benchmark & Score.** A new **Analyze → Benchmark** page that runs a quick, fully-local CPU, memory,
  and disk benchmark and rolls the results into a single "Night City" score (0–100 index + headline points)
  shown on a neon gauge, with a performance tier (Entry / Mainstream / Fast / Elite), per-test cards (CPU
  multi-thread compute, memory copy bandwidth, disk sequential write) and a score-history trend. Runs on a
  background thread, is cancellable, and writes nothing to the network — results stay on your PC. Handy for
  "how fast is my PC?" and for checking whether a cleanup or tweak actually helped.

## [2.1.0] - 2026-06-25

### Added
- **"Ask before each backup" — confirm restore points per action.** When SystemCare is about to create a
  System Restore point before a maintenance action — "Fix all", Windows/app/driver updates, disk
  maintenance, registry clean, or the app's own self-update — it now asks first with a Yes/No prompt
  instead of creating one silently. Choose **Create restore point** to make one, or **Skip** to continue
  without. A new **Settings → Safety → "Ask before each backup"** toggle (on by default) controls this;
  turn it off to go back to creating restore points automatically, or turn restore points off entirely to
  skip them. Explicit "Create restore point" buttons stay one-click.

## [2.0.0] - 2026-06-23

### Added
- **Live system monitor — tray stats + a floating mini-widget.** Two new opt-in tools under
  **Settings → Live monitor**. *Tray stats* turns the system-tray icon into a live neon CPU meter and shows
  current CPU/RAM in its tooltip, refreshed every second. The *mini-widget* is a small always-on-top neon
  panel with live CPU / RAM / network sparklines and the hottest component temperature; it's draggable,
  remembers its position, stays out of Alt-Tab, and can be dismissed from its own close button, the tray
  menu, or Settings. Both default off and, like everything else, run fully on your PC with no network
  telemetry. The monitor samples on its own isolated source, so it never disturbs the live readouts on the
  Dashboard or System Information pages.

### Security
- **GitHub update token is now encrypted at rest.** Any token entered for private-repo updates is sealed
  with Windows DPAPI (per-user) and never written to disk in clear text; existing plaintext tokens are
  migrated automatically on first run.
- **Update installers are signature-checked before launch.** A downloaded installer's Authenticode
  signature is verified after the SHA-256 check — a tampered or untrusted signature is always rejected, and
  an optional "require signed update" setting can additionally refuse unsigned installers and pin the
  publisher.
- **Background failures are now logged.** Unhandled exceptions on background threads and unobserved task
  exceptions are captured to the crash log instead of being lost.

### Internal
- Added an automated CI pipeline (build + test on every push) with dependency update monitoring, and a
  unit-test suite covering the pure-logic units.

## [1.9.0] - 2026-06-19

### Changed
- **App-wide visual consistency pass (Design System v2).** The cyberpunk "Night City" theme is now a
  formal design system — a shared spacing/type/radius scale, semantic surface tokens, and a reusable
  component library (buttons, cards, chips, list rows, the log console, and inputs with a neon focus
  glow). Every page was migrated onto it, so section headers, chips, inset panels, consoles and buttons
  look and behave consistently across the whole app, and keyboard focus is clearly visible on inputs.
  This is a styling/resources pass only — no functional or backend changes.

### Added
- **"Reduce motion & effects" accessibility setting.** A new Settings toggle stops the animated backdrop,
  the looping neon pulses and the health-gauge breathing glow, and settles page entrances to their final
  state — easier on the eyes and lighter on battery. It applies live, in both directions, without a restart.

## [1.8.0] - 2026-06-18

### Added
- **Disk Health is now a drive-health hub.** The page leads with an overall health score and a plain-English
  summary, then a per-drive card for each physical disk showing a 0–100 health score ring and live SMART
  detail — temperature, SSD wear, power-on hours, and bad-sector/uncorrectable-error counts (read from the
  Windows storage reliability counters, with per-drive temperature from the sensor backend).
- **Predictive alerts.** Urgency-ranked warnings surface when a drive is at risk (failing SMART, bad sectors,
  high wear, high temperature, or a nearly-full volume), each with a one-tap action. Critical issues also
  raise a tray notification and recommend creating a restore point.
- **One-click maintenance.** "Optimize all drives" creates a restore point, runs the right optimization per
  media (TRIM for SSDs, defrag for HDDs), does a read-only error check, and clears junk — all streamed live
  and cancelable.
- **Free-up-space action center.** Quick cards jump straight to Cleanup, Duplicate Finder, and the Disk
  Analyzer. The SFC/DISM system-file repairs now live under an "Advanced repair" section.

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

[1.9.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.9.0
[1.8.0]: https://github.com/WifeInLantern/SystemCare/releases/tag/v1.8.0
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
