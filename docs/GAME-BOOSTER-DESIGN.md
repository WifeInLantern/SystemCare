# Game Booster — Design & Implementation Spec

> Redesign of SystemCare's **Game Mode** into a premium-grade **Game Booster**, targeting parity-or-better
> with Razer Cortex while prioritising stability, reversibility, and documented Windows techniques.
> Author lens: senior Windows systems engineer / C++/C# low-level optimisation architect.

---

## 1. Executive overview

Today's **Game Mode** (`GameModeService` → `BoostService` + `MemoryOptimizerService`) does three things:
switches to the High Performance power plan, suspends a user-picked set of background apps, and trims every
process's working set with `EmptyWorkingSet`, plus an optional toast-notification silence. It is reversible
and safe, but it is *manual*, *coarse* (it trims the game too), and shallow (no service/task/scheduler/GPU/
network layers, no automatic detection, no session journal).

**Game Booster** keeps everything that already works and adds:

- A **reversible-optimisation engine** where every change is an object with `Apply()`/`Revert()` and a
  persisted **rollback journal**, so an app crash or power loss mid-session still fully restores the system
  on next launch.
- **Automatic game detection** (fullscreen-app signal + game catalog + WMI process events) that boosts on
  launch and restores on exit — no clicks.
- **Game-aware memory** that trims *everything except the game and its children*, plus an optional
  standby-list purge.
- **Deeper, still-safe layers**: whitelisted service pausing, temporary scheduled-task disabling, MMCSS +
  `Win32PrioritySeparation` scheduling tweaks, per-game GPU preference, network-throttling relief.
- A **modern UI**: one big toggle, a live status ring, real-time indicators, and a "what changed" audit log.

Design rule throughout: **documented and reversible beats aggressive**. Anything undocumented (standby purge,
timer resolution, Nagle) is isolated behind an **Advanced** flag, off by default, each with an explicit
trade-off. No process is ever *terminated* — only *suspended* (reversible). Critical Windows processes and
services are protected by hard denylists.

---

## 2. Complete Game Booster architecture

### 2.1 Component map (new + reused)

```
GameBoosterPage (XAML)            ── UI: toggle, status ring, live indicators, audit log, settings
  └─ GameBoosterViewModel         ── state, commands, real-time binding
       └─ IGameBoosterService     ── orchestrator (replaces IGameModeService)
            ├─ IOptimizationEngine        ── runs an ordered pipeline of IReversibleOptimization
            │     ├─ RollbackJournal      ── persists prior-state to %AppData%\SystemCare\gamebooster\session.json
            │     └─ [ IReversibleOptimization ] :
            │           PowerPlanOptimization      (reuse IPowerPlanService)
            │           MemoryOptimization         (reuse IMemoryOptimizerService, game-aware)
            │           StandbyPurgeOptimization   (Advanced)
            │           AppSuspendOptimization      (reuse BoostService suspend logic)
            │           ServicePauseOptimization    (reuse IServiceControlService, whitelist)
            │           ScheduledTaskOptimization    (reuse IScheduledTaskManagerService)
            │           CpuSchedulingOptimization    (registry: Win32PrioritySeparation, MMCSS)
            │           GameProcessPriorityOptimization (SetPriorityClass on the game)
            │           GpuPreferenceOptimization     (per-game UserGpuPreferences)
            │           NetworkThrottleOptimization   (MMCSS NetworkThrottlingIndex, Advanced: Nagle)
            │           NotificationOptimization      (reuse ToastEnabled + OS Focus Assist)
            │           TimerResolutionOptimization   (Advanced)
            ├─ IGameWatcherService        ── auto-detect launch/exit (fullscreen signal + catalog + WMI)
            ├─ IGameProfileService        ── per-game profiles (JSON), aggressiveness presets
            └─ ILogService / IHistoryService  ── existing logging + activity history
```

Everything under `IReversibleOptimization` is DI-registered exactly like the current services (singletons for
state, per the app's convention).

### 2.2 The core contract

```csharp
public interface IReversibleOptimization
{
    string Id { get; }                 // "power.plan", "mem.trim", "svc.pause", ...
    OptimizationTier Tier { get; }     // Safe | Advanced
    bool IsSupported(GameSession ctx); // e.g. Ultimate Perf only on AC power

    /// Capture prior state, apply the change. Returns a JSON-serialisable rollback token.
    Task<OptimizationRecord> ApplyAsync(GameSession ctx, CancellationToken ct);

    /// Restore exactly what ApplyAsync captured. Must be idempotent and never throw fatally.
    Task RevertAsync(OptimizationRecord record, CancellationToken ct);
}

public enum OptimizationTier { Safe, Advanced }
```

`OptimizationRecord` holds `{ Id, AppliedAtUtc, PriorState (object), Detail }`. The engine writes each record to
the journal **before** the next optimisation runs, so partial sessions are always recoverable.

### 2.3 Why an engine instead of the current inline flow

The current `BoostService` hard-codes order and rollback in one method. That is fine for 3 steps; it does not
scale to 12 and cannot recover from a crash. The engine gives: (1) deterministic ordered apply, reverse-order
revert; (2) per-item support checks (skip Ultimate Performance on battery, skip GPU preference on single-GPU);
(3) a durable journal; (4) a natural place to attach per-optimisation logging and the UI audit list.

---

## 3. Optimization pipeline

**Apply order** (chosen so cheap/safe wins land first and the game gets priority *after* the environment is
quiet):

1. `NotificationOptimization` (silence first — no toasts during the rest)
2. `PowerPlanOptimization` (High/Ultimate Performance)
3. `ServicePauseOptimization` (quiet the disk/telemetry)
4. `ScheduledTaskOptimization` (stop interrupters)
5. `AppSuspendOptimization` (freeze background talkers)
6. `MemoryOptimization` (trim *after* apps are suspended → reclaims their pages too; **excludes the game**)
7. `StandbyPurgeOptimization` (Advanced, once)
8. `CpuSchedulingOptimization` (MMCSS + priority separation)
9. `GameProcessPriorityOptimization` (raise the game to High)
10. `GpuPreferenceOptimization` (per-game high-performance GPU)
11. `NetworkThrottleOptimization`
12. `TimerResolutionOptimization` (Advanced)

**Revert order** is the exact reverse, each from its journal record. Revert is triggered by: manual toggle-off,
game-exit detection, app shutdown, or **journal replay on next launch** if the previous session didn't close
cleanly.

Each step is best-effort and independent: a failure in step *n* logs and continues; it never blocks revert of
the others.

---

## 4. Memory optimization subsystem

Goal: reclaim genuinely unused memory **without touching the game's resident pages** (trimming the game causes
hard page faults → stutter — the single biggest flaw in naïve "RAM boosters," including some Cortex presets).

| Technique | API | Tier | Reversible? | Trade-off |
|---|---|---|---|---|
| Working-set trim of *non-game* processes | `EmptyWorkingSet` (psapi) — **already in `NativeMethods`** | Safe | Self-healing (pages fault back on demand) | Trimming a busy app briefly costs it faults; **must exclude the game + children + protected set** |
| Keep game pages resident | `SetProcessWorkingSetSizeEx(h, min, max, QUOTA_LIMITS_HARDWS_MIN_ENABLE)` | Safe | Restore prior min/max on exit | Raising min reserves RAM; use modestly, only if free RAM is high |
| Purge standby list (cached file pages) | `NtSetSystemInformation(SystemMemoryListInformation, &cmd=MemoryPurgeStandbyList)` | **Advanced** | N/A (cache rebuilds naturally) | **Undocumented** (RAMMap/ISLC use it). Needs `SeProfileSingleProcessPrivilege` + admin. Purging cold-starts the cache → next asset loads are slower. Do **once at boost start**, never on a loop |
| Report, don't force | `GlobalMemoryStatusEx` — **already present** | Safe | — | Drives the UI "before/after free RAM" indicator |

**Key change vs current `MemoryOptimizerService`:** it trims *every* process including the game. Game Booster
passes a `GameSession.ProtectedPids` set (game PID + child PIDs discovered via the process tree) and the
existing `ProtectedNames`, and `EmptyWorkingSet` is skipped for those.

```csharp
// Game-aware trim (extends the existing OptimizeAsync)
foreach (var p in Process.GetProcesses())
{
    if (p.Id <= 4 || ProtectedNames.Contains(p.ProcessName)) continue;
    if (session.ProtectedPids.Contains(p.Id)) continue;      // <-- new: never trim the game/children
    TryEmptyWorkingSet(p.Id);
}
```

**vs Razer Cortex:** Cortex's "Booster" trims working sets and can clear standby similarly; it does **not**
reliably exclude the running game, and its "RAM freed" figure is mostly cosmetic (Windows would reclaim under
pressure anyway). Our advantage: game exclusion + honest before/after telemetry + standby purge gated behind
an explicit Advanced toggle with the cache-cold caveat surfaced in the UI.

---

## 5. Process and service management

### 5.1 Background app suspension (reuse existing, harden)

Reuse `BoostService`'s `NtSuspendProcess`/`NtResumeProcess` with its already-correct **suspend-count guard**
(don't double-suspend). Improvements:

- **Auto-target** the known background talkers (`chrome`, `msedge`, `Discord`, `Spotify`, `Steam`,
  `OneDrive`, launchers…) already listed in `GameModeViewModel.CommonBackground`, but **never** the game's own
  overlay/helpers. Suspending Discord kills push-to-talk mid-match, so overlay/voice apps are **opt-in**, not
  default.
- Hard **critical-process denylist** (existing `ProtectedNames`: `csrss`, `wininit`, `winlogon`, `services`,
  `lsass`, `smss`, `dwm`, `explorer`, `System`…). Suspending any of these is refused at the API boundary.
- **Never terminate.** Suspension is fully reversible; termination is not.

**vs Cortex:** Cortex closes/suspends background apps too, but has historically *terminated* some — which loses
unsaved work. Suspend-only is our deliberate stability advantage; trade-off is that a suspended app still holds
its RAM (mitigated because we trim *after* suspending, reclaiming its working set).

### 5.2 Service pausing (new, strictly whitelisted)

Reuse `IServiceControlService` (Stop/Start + query). Only an **allow-list** of demonstrably non-essential
services may be paused; everything else is untouched.

| Service (key) | Why pause during a game | Safe to stop? |
|---|---|---|
| `SysMain` (SuperFetch) | Stops background prefetch disk churn | Yes — restart on exit |
| `WSearch` (Windows Search) | Prevents indexer disk/CPU spikes | Yes |
| `Spooler` (Print) | Rarely needed while gaming | Yes (skip if a print job is queued) |
| `DiagTrack` (telemetry) | Cuts background upload/CPU | Yes |
| `WSearch`, `MapsBroker`, `Fax`, `RetailDemo` | Idle background | Yes |

**Never** in the list: `RpcSs`, `DcomLaunch`, `RpcEptMapper`, `Power`, `BrokerInfrastructure`, `LSM`,
`ProfSvc`, `AudioSrv`/`Audiosrv` (you want game audio!), `nvlddmkm`/GPU services, networking core. The engine
also refuses any service with dependents outside the allow-list.

For each paused service, the record stores `{ name, priorStatus, priorStartType }`. Revert restarts it and
restores start type. Some services (e.g. `SysMain`) may be auto-restarted by Windows; revert treats "already
running" as success.

```csharp
record ServiceState(string Name, ServiceControllerStatus Status, ServiceStartMode StartMode);
// Apply:  if (running && InAllowList(name) && !HasExternalDependents(name)) Stop(name);
// Revert: if (priorStatus == Running) Start(name);
```

**vs Cortex:** Cortex's "Services" tweaks are broad and persistent (they change start types until you undo
them). Ours are **session-scoped and auto-reverted**, with a tighter allow-list — safer, though slightly less
"aggressive."

### 5.3 Scheduled tasks (reuse the new `IScheduledTaskManagerService`)

Temporarily disable a small set of *interrupting* tasks for the session and re-enable on exit:

- `\Microsoft\Windows\Defrag\ScheduledDefrag`
- `\Microsoft\Windows\Windows Defender\Windows Defender Scheduled Scan` (only defer the *scheduled* scan;
  real-time protection stays on — we never disable AV)
- `\Microsoft\Windows\UpdateOrchestrator\*` scan tasks
- OneDrive / vendor updater standalone tasks

API: `Microsoft.Win32.TaskScheduler` (already referenced) `Task.Enabled = false/true`, journaled per task.
**Trade-off:** we only *disable for the session*; the journal guarantees re-enable even after a crash. We never
touch real-time Defender or anything security-relevant.

---

## 6. CPU, GPU, and storage optimization

### 6.1 CPU scheduling (documented registry + MMCSS)

| Tweak | Location / API | Tier | Reversible | Trade-off |
|---|---|---|---|---|
| Foreground boost | `HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation = 0x26` | Safe | Restore prior DWORD | Favours foreground (the game). Negligible risk; classic, documented |
| MMCSS "Games" task | `HKLM\...\CurrentVersion\Multimedia\SystemProfile\Tasks\Games` → `GPU Priority=8`, `Priority=6`, `Scheduling Category="High"`, `SFIO Priority="High"` | Safe | Restore prior values | Only benefits titles that register with MMCSS/DWM; harmless otherwise |
| System responsiveness | `...\Multimedia\SystemProfile\SystemResponsiveness = 10` (from default 20) | Safe | Restore prior | Gives multimedia threads more CPU headroom; 0 is aggressive, 10 is a safe middle |
| Game process priority | `SetPriorityClass(hGame, HIGH_PRIORITY_CLASS)` (kernel32) | Safe | Restore prior class on exit | **Never `REALTIME_PRIORITY_CLASS`** (starves input/audio/OS). High is the ceiling |
| Timer resolution | `timeBeginPeriod(1)` (winmm) / `NtSetTimerResolution` | **Advanced** | `timeEndPeriod(1)` | On Win11 timer resolution is **per-process**, so a global tweak has limited effect; raises power draw. Marginal latency benefit — off by default |

CPU affinity pinning is **intentionally excluded**: modern schedulers + game engines handle affinity better
than a blunt pin, and mispinning hurts. (Cortex exposes affinity; we consider it a footgun.)

### 6.2 GPU

| Tweak | Location / API | Tier | Notes |
|---|---|---|---|
| Per-game GPU preference | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, value name = full game exe path, data = `"GpuPreference=2;"` (2 = High Performance) | Safe, reversible | This is exactly what **Settings ▸ Display ▸ Graphics** writes — fully documented. Big win on dual-GPU laptops (forces the dGPU) |
| Hardware-Accelerated GPU Scheduling (HAGS) | `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode = 2` | **Recommendation only** | Requires a **reboot** and a supported GPU/driver → we *detect and recommend*, never silently toggle mid-session |

We do **not** touch MSI-mode / undocumented GPU registry keys (instability risk).

**vs Cortex:** Cortex's GPU story is mostly "defrag/optimise" marketing; per-app GPU preference via the
documented `UserGpuPreferences` key is a concrete, reversible advantage, especially on Optimus/AMD-switchable
laptops.

### 6.3 Storage / I/O

There is no safe "make the SSD faster" runtime switch, so we improve I/O **responsiveness indirectly**:

- Pause `SysMain` + `WSearch` + defrag task (above) → removes the biggest background disk contenders.
- Optional **pre-session cache cleanup** by reusing the existing `JunkScanService` / `DeepCleanupService`
  (temp, shader cache staleness) — frees space and reduces stale-cache thrash. Safe/reversible-N/A (deletes
  junk to Recycle Bin where applicable).
- Optionally set the game's **I/O priority** normal-high via `NtSetInformationProcess(ProcessIoPriority)` —
  **Advanced/undocumented**, off by default.

We deliberately avoid "disk optimize/defrag during play" (Cortex-style) — defragging an SSD is pointless and
defragging an HDD mid-game causes the exact stutter we're preventing.

---

## 7. Network optimization

| Tweak | Location / API | Tier | Reversible | Trade-off |
|---|---|---|---|---|
| Disable multimedia network throttling | `HKLM\...\Multimedia\SystemProfile\NetworkThrottlingIndex = 0xFFFFFFFF` (default `10`) | Safe | Restore `10` | MMCSS caps non-multimedia packets to ~10k/s under multimedia load; lifting it helps some titles. Documented |
| Nagle's algorithm off (per NIC) | `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}` → `TcpAckFrequency=1`, `TCPNoDelay=1` | **Advanced** | Delete/restore both values | Lowers small-packet latency for some games; can add overhead/retransmit cost. Controversial → opt-in, per-interface, journaled |
| DNS flush | `DnsFlushResolverCache` — **already in `NativeMethods`** | Safe | N/A | Clears stale entries; trivial benefit, zero risk |
| Fast DNS (cross-feature) | reuse the new **Secure DNS** feature (`netsh interface ipv4 set dnsservers`) | Safe | Revert to prior/DHCP | Optional synergy; keep it a *suggestion*, not automatic |

We **do not** disable TCP autotuning or apply "gamer" `netsh` bundles wholesale — most are myths or net-negative
on modern Windows. Every network change is per-interface and journaled.

**vs Cortex:** Cortex has no real network layer. This is a clean, modest, reversible advantage — sold honestly
("latency relief for some titles," not "boosts your internet").

---

## 8. Safety and rollback system

Non-negotiables, enforced structurally:

1. **Never terminate a process.** Only `NtSuspendProcess` (reversible). Termination is not in the codebase.
2. **Critical-entity denylists** at the API boundary: process names (`csrss`, `wininit`, `winlogon`,
   `services`, `lsass`, `smss`, `dwm`, `System`, `Registry`, `Memory Compression`, `Secure System`…) and a
   service **allow-list** (only listed services can be paused; anything with external dependents is refused).
3. **Complete rollback plan per change.** Every `ApplyAsync` returns an `OptimizationRecord` capturing prior
   state *before* mutating. No change is applied without a matching revert path.
4. **Durable session journal.** Records are written to `%AppData%\SystemCare\gamebooster\session.json` as they
   apply. On startup the app checks for an unclosed journal and **replays reverts** ("SystemCare restored your
   system after an interrupted Game Booster session"). This is the key resilience upgrade over the current
   in-memory-only rollback.
5. **Restore-point option.** For users who enable Advanced tweaks, offer a one-time restore point via the
   existing `IRestorePointService` + `IBackupConfirmationService` before the *first* advanced session (heavy,
   so not per-session).
6. **Idempotent revert.** Reverting twice, or reverting something already reverted, is a no-op. Handles the
   race between manual toggle-off and auto game-exit firing together.
7. **Watchdog.** A lightweight timer re-asserts that the game is still alive; if the process is gone and the
   exit event was missed, the watchdog triggers revert.

```csharp
// Engine apply/revert skeleton
async Task ApplyAllAsync(GameSession s, CancellationToken ct) {
    foreach (var opt in _pipeline.Where(o => o.Tier == Safe || s.AdvancedEnabled)) {
        if (!opt.IsSupported(s)) { Log(opt.Id, "skipped: unsupported"); continue; }
        try { var rec = await opt.ApplyAsync(s, ct); _journal.Append(rec); Log(opt.Id, "applied", rec.Detail); }
        catch (Exception ex) { Log(opt.Id, "apply failed", ex.Message); }   // continue; never abort
    }
}
async Task RevertAllAsync(CancellationToken ct) {
    foreach (var rec in _journal.Read().AsEnumerable().Reverse()) {
        try { await Resolve(rec.Id).RevertAsync(rec, ct); }
        catch (Exception ex) { Log(rec.Id, "revert failed", ex.Message); }  // continue; best-effort
    }
    _journal.Clear();
}
```

**vs Cortex:** Cortex reverts on game exit but its changes to services/registry can persist or drift, and a
crash can leave the system in the "boosted" state. The persisted journal + startup replay is a concrete
reliability advantage.

---

## 9. Automatic game detection

Three complementary signals, cheapest first:

1. **Fullscreen / D3D signal (documented, low-cost):** poll `SHQueryUserNotificationState` (shell32). A return
   of `QUNS_RUNNING_D3D_FULL_SCREEN` or `QUNS_BUSY` (fullscreen app) is a strong "a game is running" hint and is
   exactly what Windows itself uses to auto-enable Focus Assist. Cheap enough to poll every 2–3 s.
2. **Game catalog match:** maintain a curated list of game executables + detect installs from **Steam**
   (`steamapps\libraryfolders.vdf` → `appmanifest_*.acf`), **Epic**, **Xbox**, and common `Program Files`
   game dirs. Match running process `ExecutablePath` against it (avoids false positives from any random
   fullscreen app like a video player — combine signal 1 AND catalog for auto-activation; signal 1 alone only
   *suggests*).
3. **Process lifecycle events:** a `ManagementEventWatcher` on
   `SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process'` (and the deletion
   event) to catch launch/exit promptly. Trade-off: WMI event watching has measurable overhead; offer a
   **lightweight 2 s `Process.GetProcesses` poll** as the default and WMI as an opt-in for instant detection.

On confirmed launch → resolve the game's process tree into `ProtectedPids`, load its **profile** (or the
default preset), and run the pipeline. On exit (event or watchdog) → revert. All of this lives in a singleton
`GameWatcherService` started at app launch (like the existing `ResourceAlertService`), gated by a
"Boost automatically" setting.

**vs Cortex:** Cortex's auto-detect relies on its own game database + install scans (same idea). Parity here;
our edge is combining the documented fullscreen signal to avoid boosting on non-games, and reusing the app's
existing background-service pattern.

---

## 10. Logging and diagnostics

- **Per-session structured log** via existing `ILogService`: one line per optimisation with
  `applied|skipped|failed`, before/after where measurable (RAM free, power plan, #apps, #services, #tasks).
- **Activity History** via existing `IHistoryService`: a single summary row per session (start, duration,
  RAM freed, counts) — consistent with how Boost/Cleanup already record.
- **Live session object** the UI binds to (see §11): current game, elapsed time, each layer's state.
- **Optional FPS/latency telemetry (Advanced):** integrate **PresentMon** (Intel, redistributable) as an
  external helper to capture real FPS/frame-time and present-latency for a genuine before/after. This is the
  only credible way to show *measured* gains; marked Advanced because it ships an extra binary. We do **not**
  fabricate FPS numbers.

**vs Cortex:** Cortex's "FPS" overlay is its headline. Matching it *honestly* (PresentMon) rather than
inventing numbers is both a technical and trust advantage.

---

## 11. UI/UX redesign

Match the app's cyberpunk system (`CyberPageTitle`, `HealthGauge`, `ui:Card`, `TaskProgress`, subtle brushes).

**Layout**

- **Hero card:** a large **Activate Game Booster** toggle + a neon **status ring** (reuse `HealthGauge`
  styling) that reads *Idle → Watching → Boosting*. When boosting, the ring shows the active game name.
- **Real-time indicator row** (live-bound to the session): Power plan · RAM freed · Apps paused · Services
  paused · Tasks deferred · Notifications · GPU pref · Detection state. Each is a small chip with a
  green/amber state, mirroring the Security/Defender chip style already in the app.
- **"What changed" audit list:** the journal rendered live — every optimisation with applied/skipped/failed,
  expandable, so the user sees *exactly* what was touched and that it will be restored.
- **Settings drawer:**
  - **Aggressiveness preset:** *Balanced* (Safe tier only) / *Aggressive* (adds Advanced). Balanced is default.
  - **Auto-boost** on game launch (on/off) + detection mode (poll/WMI).
  - **Manage apps/services** whitelist (which to suspend/pause).
  - **Per-game profiles:** override preset, GPU preference, and app/service selections per title.
  - **Advanced toggles** each with an inline trade-off caption (standby purge, timer resolution, Nagle).
- **Restore-everything** button always visible while active (manual panic revert).

**Real-time status** comes from the `GameSession`/journal raising `PropertyChanged`; the VM exposes observable
counts exactly like the current `GameModeViewModel` already does for `IsActive`/`StatusText`.

**Rename:** the nav item, page title, tooltips, `Views/GameModePage.*`, `ViewModels/GameModeViewModel.cs`,
`Services/GameModeService.cs`, history category strings, and CHANGELOG all move from **Game Mode → Game
Booster** (keep the class rename mechanical; preserve the `IBoostService` engine underneath).

---

## 12. Implementation roadmap

**Phase 0 — Rename & scaffold (low risk).** Game Mode → Game Booster across UI/VM/service/nav/history. Introduce
`IReversibleOptimization`, `OptimizationRecord`, `RollbackJournal`, and wrap the *existing* three behaviours
(power, suspend, memory, notifications) as engine optimisations. Behaviour-identical; now journaled + crash-safe.

**Phase 1 — Game-aware memory + safe layers.** Add game/child PID exclusion to memory trim; add
`ServicePauseOptimization` (whitelist), `ScheduledTaskOptimization`, `CpuSchedulingOptimization`
(`Win32PrioritySeparation` + MMCSS), `GameProcessPriorityOptimization`. All Safe tier, all reversible.

**Phase 2 — Auto detection.** `GameWatcherService` (fullscreen signal + poll, catalog from Steam/Epic), auto
apply/revert, watchdog, journal replay on startup.

**Phase 3 — GPU/network + profiles.** `GpuPreferenceOptimization`, `NetworkThrottleOptimization`,
`IGameProfileService` (per-game JSON), aggressiveness presets, settings drawer.

**Phase 4 — Advanced + diagnostics.** Standby purge, timer resolution, Nagle (all Advanced, off by default,
each with UI caveat), HAGS recommendation, optional PresentMon telemetry.

**Phase 5 — Hardening.** Unit tests for the engine (apply→journal→revert round-trips, denylist enforcement,
idempotent revert), fuzz the journal replay, verify no critical process/service is ever touched.

---

## 13. Example pseudocode (representative optimisations)

```csharp
// Ultimate/High Performance power plan (reuses IPowerPlanService)
sealed class PowerPlanOptimization(IPowerPlanService power) : IReversibleOptimization {
    public string Id => "power.plan"; public OptimizationTier Tier => OptimizationTier.Safe;
    public bool IsSupported(GameSession s) => true;
    public Task<OptimizationRecord> ApplyAsync(GameSession s, CancellationToken ct) {
        var prior = power.GetActiveScheme();
        var target = s.OnAcPower ? power.UltimateOrHighPerformanceGuid() : power.HighPerformanceGuid;
        power.SetActiveScheme(target);
        return Task.FromResult(new OptimizationRecord(Id, prior));   // prior GUID captured
    }
    public Task RevertAsync(OptimizationRecord r, CancellationToken ct) {
        if (r.PriorState is Guid g) power.SetActiveScheme(g); return Task.CompletedTask;
    }
}

// Win32PrioritySeparation foreground boost (documented registry)
sealed class CpuSchedulingOptimization : IReversibleOptimization {
    const string Key = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    public Task<OptimizationRecord> ApplyAsync(GameSession s, CancellationToken ct) {
        using var k = Registry.LocalMachine.CreateSubKey(Key);
        var prior = k.GetValue("Win32PrioritySeparation");         // capture first
        k.SetValue("Win32PrioritySeparation", 0x26, RegistryValueKind.DWord);
        return Task.FromResult(new OptimizationRecord(Id, prior));
    }
    public Task RevertAsync(OptimizationRecord r, CancellationToken ct) {
        using var k = Registry.LocalMachine.CreateSubKey(Key);
        if (r.PriorState is int v) k.SetValue("Win32PrioritySeparation", v, RegistryValueKind.DWord);
        else k.DeleteValue("Win32PrioritySeparation", false);
        return Task.CompletedTask;
    }
}

// Standby list purge (Advanced, undocumented, admin + privilege)
static void PurgeStandbyList() {
    // requires SeProfileSingleProcessPrivilege enabled on the token
    int cmd = MemoryPurgeStandbyList; // = 4
    NativeMethods.NtSetSystemInformation(SystemMemoryListInformation /*0x50*/, ref cmd, sizeof(int));
}
```

`NativeMethods` additions needed: `SetProcessWorkingSetSizeEx`, `SetPriorityClass`, `NtSetSystemInformation`,
`timeBeginPeriod/timeEndPeriod`, `SHQueryUserNotificationState`, and privilege helpers
(`OpenProcessToken`/`AdjustTokenPrivileges` for `SeProfileSingleProcessPrivilege`). The suspend/trim/memory
P/Invokes already exist.

---

## 14. Comparison vs Razer Cortex

| Capability | Razer Cortex | Game Booster (this design) | Our edge / trade-off |
|---|---|---|---|
| RAM trim (working sets) | Yes (trims broadly) | Yes, **excludes game + children** | Avoids self-inflicted stutter; honest before/after |
| Standby-list purge | Yes | Yes (**Advanced**, once, caveated) | Same API; we surface the cache-cold trade-off |
| Background app handling | Suspend/**close** (can lose work) | **Suspend only** (reversible) | Safer; suspended apps' RAM reclaimed by trim-after-suspend |
| Service tweaks | Broad, often **persistent** | Whitelisted, **session-scoped**, auto-revert | Safer; slightly less aggressive |
| Scheduled-task control | Limited | Defers defrag/scan/update tasks, journaled | Reversible even after a crash |
| Power plan | High Performance | High / **Ultimate** (AC-aware) | Battery-aware; reversible |
| CPU scheduling | Priority/affinity | `Win32PrioritySeparation` + MMCSS + game→High priority (**no affinity pin, no realtime**) | Documented, safer defaults |
| GPU | "Optimise" marketing | **Per-game GPU preference** (documented key) + HAGS *recommendation* | Real dGPU forcing on laptops; no risky GPU hacks |
| Network | None meaningful | MMCSS throttling relief + optional Nagle (Advanced), per-NIC journaled | Modest, honest, reversible |
| Notifications | Focus/none | `ToastEnabled` + OS Focus Assist | Reuses OS mechanism |
| Auto game detection | Own game DB | Fullscreen signal **+** catalog **+** WMI/poll | Fewer false positives |
| Rollback resilience | Revert on exit; can drift | **Durable journal + startup replay + watchdog** | Survives crash/power loss |
| FPS/latency metrics | In-house overlay | Optional **PresentMon** (real data) | Honest measurement, no invented numbers |
| Safety posture | Aggressive by default | **Safe by default, Advanced opt-in**, hard denylists, never terminate | Stability-first |

---

## 15. Final recommendations for a production-ready Game Booster

1. **Ship Phase 0 first.** The rename + engine + journal is low-risk and immediately makes today's behaviour
   crash-safe. Everything else layers on without reworking it.
2. **Default to Safe.** Balanced preset (Safe tier) should deliver 90% of the felt benefit — quiet background,
   High/Ultimate power, game→High priority, MMCSS, notifications off — with essentially zero stability risk.
   Keep Advanced genuinely optional and always caveated.
3. **Exclude the game from every "cleanup."** The one rule that separates a real booster from a placebo:
   never trim, deprioritise, or throttle the game or its children.
4. **Make reversibility a hard invariant, tested.** Round-trip unit tests (apply→journal→revert), denylist
   enforcement tests, idempotent-revert tests, and a startup-replay test. A booster that can't cleanly undo
   itself is a liability.
5. **Measure honestly.** Wire PresentMon for real FPS/frame-time deltas; show "before/after free RAM" from
   `GlobalMemoryStatusEx`. Never display a fabricated FPS gain.
6. **Prefer documented mechanisms** (power plans, `UserGpuPreferences`, MMCSS, `Win32PrioritySeparation`,
   `NetworkThrottlingIndex`, `EmptyWorkingSet`, Focus Assist) and quarantine the few undocumented ones
   (standby purge, timer resolution, Nagle) behind Advanced with explicit trade-offs.
7. **Respect the user's machine.** Battery-aware power, opt-in suspension of voice/overlay apps, a visible
   "Restore everything" button, and a transparent audit log — trust is the premium feature.
