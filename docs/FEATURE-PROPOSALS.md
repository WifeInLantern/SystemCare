# SystemCare — Feature Proposals (post-2.13.0)

Grounded in a full read of the app (49 pages, ~75 services) on 2026-07-11. Each proposal names the
existing infrastructure it builds on, so nothing here is a from-scratch bet. Excludes the UI/IA work
already planned in `docs/UI-REDESIGN-V5.md` and the engineering debt in `docs/PROGRAM-ANALYSIS.md`.

---

## Tier 1 — high value, fits existing infrastructure

### 1. Security-aware Software Updater (CVE flagging)
Tag pending app updates by security severity by cross-referencing installed name/version against a
vulnerability feed (OSV.dev has a free, keyless JSON API). "Update all" gains a "Security updates
first" mode; the Dashboard security posture and health score can count known-vulnerable apps.
**Builds on:** `SoftwareUpdateService`, `WingetListParser`, `HealthScoreService`, `BreachCheckService`
(already does the k-anonymity HTTP pattern — same privacy discipline applies: query by package name
only, no inventory uploaded).
**Why:** turns the updater from convenience into a protection feature — the single strongest
differentiator vs. every generic "PC cleaner" in the category.

### 2. Autorun Guard — alert on new startup entries
Snapshot startup entries (Run keys, startup folders, scheduled tasks) after each session; on next
scan or on a timer, diff and raise a tray notification: "PhotoEditor added itself to startup —
Allow / Disable." One click disables via the existing `StartupApproved` mechanism (reversible).
**Builds on:** `StartupManagerService`, `ResourceAlertService` (alert plumbing), `TrayIconService`,
`HistoryService` (log the decision).
**Why:** silent startup creep is *the* thing users buy this category of app to stop; today the app
only shows the list when you look at it.

### 3. App Cache Cleaner (beyond browsers)
A curated, whitelisted catalog of per-app caches safe to purge: Discord, Spotify, Teams, Slack,
Steam shader caches, and a "developer" group (npm/yarn/pip/NuGet/Gradle caches, Docker build cache
detection). Same dry-run → Recycle-Bin-where-possible discipline as Junk Cleanup.
**Builds on:** `JunkScanService` architecture, `SafeFileEnumerator`, `PathExclusionMatcher`; the
catalog pattern mirrors `SoftwareHubCatalog`.
**Why:** these caches are routinely multi-GB — bigger real-world wins than another pass over temp
files, and no competitor handles the developer group well.

### 4. Storage Forecast — "days until full"
Per-drive free-space history is already recordable; add a trend line and a linear-fit forecast
("C: full in ~34 days at current rate") on the Dashboard drive cards and Care Report, with an alert
threshold.
**Builds on:** `HealthTrendService`, `CareReportPage` charts, `DriveMetrics`, `ResourceAlertService`.
**Why:** converts the passive drive bar into foresight; cheap to build, feels premium.

### 5. Undo Center — one timeline for every reversible action
A single page listing recent SystemCare actions across tools — registry backups, tweak toggles,
startup disables, DNS changes, debloat removals, restore points — each with its own Revert button.
Rescue Center remains the restore-point subset; this is the umbrella.
**Builds on:** `HistoryService` (already records everything), per-service revert paths that already
exist (`.reg` restore, `StartupApproved` re-enable, tweak toggles, `DnsService` Automatic reset).
**Why:** reversibility is the app's stated identity ("Safety first"); today it's scattered across
five pages. One Undo Center makes the promise visible and auditable.

---

## Tier 2 — strong additions, moderate effort

### 6. Similar-media finder (perceptual duplicates)
Extend Duplicate Finder with a perceptual-hash mode (dHash/aHash) for images: near-identical photos,
resized copies, edited exports. Group view with "keep highest resolution" preselection.
**Builds on:** `DuplicateFinderService` pipeline (size → partial hash → full hash slots in a
perceptual stage naturally).

### 7. Hosts blocklist subscriptions + auto-refresh
Ad Blocker currently applies a list; add named upstream sources (StevenBlack et al.), scheduled
refresh via the existing maintenance task, and a diff preview before applying.
**Builds on:** `HostsBlockerService`, `ScheduledMaintenanceService`, `UpdateSignaturePolicy` pattern
for verifying downloads.

### 8. Browser extension audit
Enumerate installed extensions (Chrome/Edge/Firefox profile manifests are on-disk JSON), show
permissions, flag high-risk combinations (e.g. "read all sites" + clipboard), link to store pages
for removal.
**Builds on:** `BrowserModels`/`BrowserCleanupService` profile discovery.

### 9. Idle-triggered Auto Care
Beyond daily/weekly: "run maintenance when the PC is idle ≥ N minutes and on AC power." Uses the
scheduled task's idle trigger — the Task Scheduler wrapper is already in the dependency list.
**Builds on:** `ScheduledMaintenanceService`, `AutoCareService`, TaskScheduler package.

### 10. Exclusions Center
One Settings section listing every path/app/registry exclusion across Junk, Duplicates, Large
Files, Privacy, and Updater ignore lists — reviewable and editable in one place.
**Builds on:** `PathExclusionMatcher`, per-tool ignore lists in `AppSettings`.

---

## Tier 3 — bigger bets (plan before committing)

### 11. CLI verbs / headless mode
`SystemCare.exe /clean /trim /report out.html` for power users and scripted fleets.
`CommandLineParser` and the silent scheduled-maintenance path already exist — this is exposure and
hardening, not invention. Pairs with the Program-Analysis test-harness investment (W2).

### 12. Crash correlation in Reliability Center
When Reliability shows a crash cluster, correlate timestamps against the app's own history: driver
installed, update applied, tweak toggled in the preceding window → "2 crashes began the day after
the display driver update — roll back?" **Builds on:** `ReliabilityService`, `HistoryService`,
`DriverUpdateService` (restore-point rollback path exists).

### 13. Wi-Fi analyzer
Signal strength, band/channel, congestion from `netsh wlan` / native WLAN API; slots into Network
tools next to Speed Test. Self-contained, medium effort.

### 14. Cloud-file advisor
Detect OneDrive/Dropbox folders where large, rarely-opened files are fully hydrated locally and
suggest "free up space" (Files On-Demand dehydration is a documented attribute operation —
reversible, nothing deleted). **Builds on:** `LargeFileScanService` last-access data.

### 15. Localization (.resx extraction)
Not a feature users see, but the gate to every non-English market; flagged as W11 in the program
analysis. Do it before the string surface grows further.

---

## Deliberately not proposed

- **Antivirus/real-time scanning** — Defender integration already covers it; competing there is a
  liability, not a feature.
- **Registry "deep clean" aggressiveness levels** — the conservative scanner is a trust asset;
  aggressive modes are where this category earns its bad reputation.
- **Telemetry/analytics on users** — the README's privacy stance is a differentiator; keep it.

## Suggested sequencing

2.14 (SHIPPED): Autorun Guard, App Cache Cleaner, Storage Forecast, crash correlation,
community blocklist, idle maintenance, CLI verbs, exclusions groundwork.

---

# Round 2 — proposals after 2.14

Still open from Round 1: **Undo Center** (#5), **similar-media finder** (#6), **browser extension
audit** (#8), **CLI report export** (#11 remainder), **Wi-Fi analyzer** (#13), **cloud-file
advisor** (#14), **security-aware updater** (#1 — still needs a data-source decision),
**localization** (#15). Those remain valid; below are the *new* ideas.

### R2-1. Greek + localization framework (was #15 — now concrete)
Extract strings to `.resx` starting with the highest-traffic surfaces (nav, Dashboard, Settings,
Cleanup) and ship **Ελληνικά** as the first language, with a Settings language picker (default:
follow Windows). Rajdhani/Orbitron have full Latin+Greek coverage, so the identity survives.
**Why now:** the string surface grows every release; each release makes this more expensive.

### R2-2. Temperature alerts (extends Sensors + ResourceAlerts)
The alert engine (CPU/RAM/disk thresholds, sustained-minutes logic, tray balloons) already exists —
add CPU/GPU temperature thresholds from `SensorMonitorService` to the same pipeline. Small, real
protection for gaming PCs and ageing laptops.

### R2-3. Boot report ("your PC started 12% slower than usual")
`BootPerformanceService` already reads boot times. Keep a per-boot history (same JSON pattern as
the trend services); when launched at startup, show one tray line comparing this boot to the 30-day
median, linking to Boot Analyzer + Startup Manager when it degrades.

### R2-4. Hibernation & pagefile advisor (new Optimize tool)
`hiberfil.sys` and `pagefile.sys` silently cost 8–32 GB. A small page: show their sizes, explain
the trade-offs in plain language, offer reversible actions (disable/resize hibernation via
`powercfg /h`, pagefile bounds via WMI) with a restore path. High space-win per line of code.

### R2-5. Accent theme picker (Cyan / Magenta / Violet)
`ApplicationAccentColorManager` + `CyberPalette` already centralize the accent. Offer the three
identity neons as selectable accents in Settings — personalization without breaking the design
system (one setting, one palette swap at startup).

### R2-6. Tray quick-actions menu
The tray icon exists; its context menu is minimal. Add: Boost, Clean junk, Free RAM, Empty Recycle
Bin, Create restore point — the Dashboard quick-action set, reachable without opening the window.

### R2-7. Scheduled Care Report export
`CareReportExporter` exists; the scheduler exists. A monthly HTML report ("what SystemCare did for
you: X GB freed, N updates, score trend") written to Documents and announced by a balloon.
Retention marketing that's also genuinely useful.

### R2-8. Windows Search index health (new tool, small)
Detect oversized/corrupt `Windows.edb`, show size, offer rebuild (documented registry+service
sequence, reversible). A classic "why is my disk full/slow" culprit no current tool covers.

### Recommended 2.15 scope
**R2-2 temperature alerts + R2-3 boot report + R2-5 accent picker + R2-6 tray menu** — four small,
high-visibility wins on existing infrastructure. Start R2-1 (Greek) in parallel as its own 2.16
release. R2-4 and the Round-1 leftovers (Undo Center first) fill 2.17+.

---

# Round 3 — after 2.18 (the app is feature-rich; value now shifts to depth and trust)

Shipped so far: 19 features + 2 polish/bug passes across 2.13–2.18. What remains valuable is
mostly *finishing the original redesign* and a few features with proven foundations.

## A. Finish the UI redesign (docs/UI-REDESIGN-V5.md, phases 3–5) — the biggest unshipped value

### R3-1. Explainable health score (v5 §5.2)
Under the Dashboard gauge, clickable chips for the score's inputs: `Junk 2.1 GB`, `Startup 9 items`,
`RAM 78%`, `Security 4/5` — each jumps to its tool; "Fix all" states its plan before running.
**Why first:** the score is the product's centerpiece and still a black box; HealthScoreService
already computes these inputs — this is surfacing, not new math.

### R3-2. Pinned tools + Recents in the nav pane
A "Pinned" group (starred via right-click or the palette) + auto "Recents" (last 3), persisted in
AppSettings. The 90% case becomes one click. Lighter than the full category-hub restructure —
which can follow later or never; with search + pins the flat list stops hurting.

### R3-3. Accessibility pass (v5 phase 4)
`AutomationProperties.Name` on icon-only buttons; `AutomationPeer` for HealthGauge ("PC health:
84 of 100"); focus visuals on custom controls; a "Solid surfaces" toggle (reduce transparency /
high-ambient-light accommodation). The score becomes perceivable to assistive tech for the first time.

### R3-4. Lock in the cleanup: SmokeTest lint
The inline-drift cleanup is ~done; add the CI rule so it can never return — SmokeTest scans
`Views/*.xaml` for `Opacity="0.` + `FontSize="` on text elements and fails the build. Ten lines of
C#; permanent payoff.

## B. New features on proven foundations

### R3-5. Similar-photos mode in Duplicate Finder
Perceptual hash (dHash) stage in the existing size→partial→full pipeline: catches resized/edited
copies exact hashing misses; "keep highest resolution" preselection.

### R3-6. Restore-point watchdog
Warn (once, tray) when System Protection is off or the newest restore point is >30 days old —
the safety net the whole app leans on, currently unmonitored. Tiny: RestorePointService + the
alert plumbing.

### R3-7. Autorun Guard: periodic re-check
Currently checks at app start only; a program installed mid-session isn't noticed until next
launch. Re-run the diff every 6h while running (timer + the existing service).

### R3-8. Undo Center (carried, still worth it)
One page listing recent reversible actions with per-item Revert. Needs history entries to carry a
machine-readable revert descriptor — a small HistoryService extension first (additive field).

## Recommended 2.19
R3-1 + R3-4 + R3-6 + R3-7 (score explainability + permanent lint + two small watchdogs).
2.20: R3-2 pins/recents. Then R3-3 a11y as its own release; R3-5/R3-8 after.

## Not recommended
Full category-hub nav restructure (pins+search deliver most of the value at a fraction of the
risk); localization (declined); cloud-file advisor (OneDrive attribute edge cases vs. modest win);
security-updater CVE flagging (still no good keyless data source for Windows desktop apps).
