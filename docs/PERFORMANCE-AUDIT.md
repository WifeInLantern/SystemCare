# SystemCare — Performance & Resource Optimization Audit

> Lens: senior performance engineer. Scope: CPU, memory, disk I/O, startup time, and idle runtime cost of the
> WPF/.NET 8 desktop app. **Evidence-based** with file references; functionality/UX preserved. Where the code
> is already optimal, that is stated rather than invented.

---

## Executive Summary

SystemCare is a **self-contained, single-file WPF/.NET 8** app (~78 MB exe bundling the runtime). It is
already disciplined in the usual WPF hot spots: the animated backdrop is frame-throttled to ~30 fps with
frozen pens/brushes and honours Reduce-motion (`Controls/CyberBackground.cs`); live metrics sample CPU via the
cheap `GetSystemTimes` P/Invoke on a **background-priority, reference-counted** 1 s timer that stops when no
page needs it (`Services/LiveMetricsService.cs`); `HttpClient` instances are `static readonly` (no socket
churn); and heavyweight resources (ETW network session, LibreHardwareMonitor sensor driver) are disposed on
exit. There is **no obvious memory leak, no busy-wait, and no per-frame allocation** in the render path.

The meaningful remaining costs are: (1) **process-spawn amplification** — spawning `powershell.exe` once per
item in bulk operations and once per status read where in-process WMI would do; (2) **startup cost** — no
ReadyToRun precompilation and single-file compression that forces a decompress-to-temp on launch; and (3)
**idle GPU/CPU** from the backdrop continuing to invalidate while the window is backgrounded. These are the
high-ROI targets below.

**Biggest wins, in order:** batch bulk PowerShell operations → ReadyToRun → pause backdrop when unfocused →
prefer WMI over PowerShell for status reads → reconsider single-file compression.

---

## Critical Bottlenecks

None that threaten stability. The items below are efficiency losses, not correctness bugs.

---

## High-Impact Optimizations

### H1 — Batch bulk PowerShell operations (bloatware removal spawns one process per app)

- **Problem:** Removing N selected apps spawns **N separate `powershell.exe` processes** — the VM loops and
  calls `UninstallAsync` per item (`ViewModels/BloatwareViewModel.cs:103` → `Services/AppPackageService.cs`
  `RunPowerShellExit` per app). Each spawn costs ~150–300 ms and a transient ~30–40 MB working set for the
  child process.
- **Root cause:** Per-item process invocation instead of one script that iterates.
- **Optimization:** Add a batch overload `UninstallManyAsync(IEnumerable<AppPackage>)` that builds a **single**
  PowerShell script iterating the names, e.g.:
  ```powershell
  $names = @('Microsoft.BingWeather','Clipchamp.Clipchamp', ...)
  foreach ($n in $names) {
    Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers -EA SilentlyContinue
    Get-AppxProvisionedPackage -Online | ? DisplayName -EQ $n | Remove-AppxProvisionedPackage -Online -EA SilentlyContinue
  }
  # emit a line per name reporting still-present/removed for the UI
  ```
  Report progress by reading streamed stdout lines instead of one exit code per app.
- **Expected impact:** Removing 20 apps drops from ~20 spawns (~4–6 s wall, ~700 MB cumulative transient
  allocation churn) to **1 spawn (~1–1.5 s, one child process)**. ~4–5× faster bulk removal, far lower peak
  memory.
- **Complexity:** Low–Medium.

### H2 — Enable ReadyToRun (R2R) precompilation

- **Problem:** The publish is JIT-only (`release-*.cmd` uses `--self-contained true -p:PublishSingleFile` with
  **no `PublishReadyToRun`**). Every method is JIT-compiled on first use → slower cold start and a CPU/memory
  spike during the first seconds of runtime.
- **Root cause:** Missing R2R flag.
- **Optimization:** Add `-p:PublishReadyToRun=true` to the publish command (and/or `<PublishReadyToRun>` in the
  csproj). Ships native-precompiled images; the JIT only handles what R2R can't.
- **Expected impact:** Typically **20–40 % faster cold start** for a WPF app and reduced warm-up CPU + less
  JIT-generated code held in memory. **Trade-off:** exe grows ~25–35 % (native images) — acceptable given it's
  already self-contained; disk is the cheap resource here.
- **Complexity:** Low (one flag). Re-test startup.

### H3 — Prefer in-process WMI over spawning `powershell.exe` for status reads

- **Problem:** `RansomwareShieldService.GetStatusAsync` and `SetEnabledAsync` shell out to
  `powershell.exe … Get-MpPreference/Set-MpPreference` (`Services/RansomwareShieldService.cs`), and
  `HostsBlockerService` shells to `ipconfig`. Each Defender/Ransomware page open = 1–2 powershell spawns.
- **Root cause:** PowerShell used where the same data is available via WMI in-process (the app already reads
  `MSFT_MpComputerStatus` directly in `DefenderService`).
- **Optimization:** Read `MSFT_MpPreference` (root\Microsoft\Windows\Defender) via `ManagementObjectSearcher`
  for `EnableControlledFolderAccess` + `ControlledFolderAccessProtectedFolders`, matching the pattern already
  in `DefenderService.GetStatusAsync`. Keep `Set-MpPreference` (write) via PowerShell (no clean WMI setter),
  but the frequent **read** path avoids a spawn. For DNS flush, the app already has `DnsFlushResolverCache`
  P/Invoke in `NativeMethods` — reuse it instead of `ipconfig /flushdns`.
- **Expected impact:** Saves ~150–300 ms + ~35 MB transient per status read; snappier page loads, lower churn.
- **Complexity:** Medium.

---

## Medium-Impact Optimizations

### M1 — Pause the animated backdrop when the window is not the foreground

- **Problem:** `CyberBackground` hooks `CompositionTarget.Rendering` while `Loaded` and invalidates at ~30 fps
  (`Controls/CyberBackground.cs:110,121`). When the window is visible-but-unfocused or on another monitor, the
  30 fps invalidate + `OnRender` (grid + gradient redraw) keeps running on the UI thread. (When minimized to
  tray the window is hidden, so paint is already skipped.)
- **Root cause:** Animation is gated on `Loaded`, not on window activation/visibility.
- **Optimization:** Also `Unhook()` on `Window.Deactivated`/`StateChanged==Minimized` and re-`Hook()` on
  `Activated`. One static frame remains drawn.
- **Expected impact:** Eliminates ~30 fps redraw (a few % of one core + GPU compositor work) whenever the app
  is open but not in front — the common "left running in background" case.
- **Complexity:** Low–Medium.

### M2 — Reconsider `EnableCompressionInSingleFile`

- **Problem:** Compression is on, so the ~78 MB bundle is **decompressed to a temp extraction directory on
  launch** (disk write + CPU) whenever the version changes, and adds decompress time to cold start.
- **Root cause:** Optimizing for download size over startup speed.
- **Optimization:** For "lowest startup time / disk I/O," set `EnableCompressionInSingleFile=false`. **Trade-off:**
  larger installer/download. This is a deliberate size-vs-speed choice — recommend **off** if startup latency is
  the priority, **on** if distribution size is. Pairs with H2 (R2R already enlarges the exe).
- **Expected impact:** Removes a one-time ~78 MB temp extraction and shaves decompress time from cold start.
- **Complexity:** Low.

### M3 — Lazy-load / release the LibreHardwareMonitor sensor stack

- **Problem:** LibreHardwareMonitor (used by `TemperatureService`/`SensorMonitorService`) opens a kernel driver
  and enumerates hardware — heavy in CPU and memory.
- **Root cause / to verify:** Ensure it is initialized **only** when the Sensors page (or temperature-dependent
  Disk Health) is first opened, and torn down when idle — not at app start.
- **Optimization:** Confirm lazy init; if the sensor computer object is created eagerly, defer it behind first
  use and `Close()` it when the last consumer navigates away (mirror `LiveMetricsService`'s consumer ref-count).
- **Expected impact:** Avoids a persistent driver + polling cost for users who never open Sensors.
- **Complexity:** Medium.

### M4 — Avoid full `Process.GetProcesses()` where a lighter query suffices

- **Problem:** `MemoryOptimizerService.OptimizeAsync` calls `Process.GetProcesses()` — allocates ~200 `Process`
  objects (each opens a handle) to trim working sets. `ProcessService` similarly for the Processes page.
- **Root cause:** Convenience API over a lighter enumeration.
- **Optimization:** On-demand actions (Free up RAM) are infrequent, so ROI is low — keep as is. For the
  Processes page **auto-refresh** (if it polls), ensure it only refreshes while the page is visible and reuses
  a single enumeration per tick. Low priority.
- **Expected impact:** Minor GC relief; only matters if a page polls `GetProcesses` on a timer.
- **Complexity:** Low.

---

## Low-Impact Optimizations

- **L1 — `InvariantGlobalization`:** setting it drops ICU load (a few MB + startup). **Not recommended** —
  the app formats bytes/dates/percentages and would lose culture-correct formatting. Skip.
- **L2 — GC mode:** keep the default **workstation, concurrent** GC. `ServerGarbageCollection` would *raise*
  memory (per-core heaps) for no desktop benefit — do **not** enable.
- **L3 — TieredCompilation / PGO:** defaults are already appropriate; `TieredPGO` (default on in .NET 8) is
  fine. No change.
- **L4 — Startup service resolution:** `App.OnStartup` resolves ~25 services eagerly. Most are cheap; only wire
  a service at startup if it must run headless (tray, scheduled-maintenance sync, Game Booster recovery). Audit
  that none pulls a heavy dependency (e.g., sensors) into the startup path.
- **L5 — Trimming:** **not available** — WPF does not support `PublishTrimmed` (documented in the README).
  Don't attempt; it breaks XAML/reflection.

---

## Resource Usage Reduction Estimates

| Optimization | CPU | Memory | Disk I/O | Startup |
|---|---|---|---|---|
| H1 batch removal | ↓↓ (N→1 process) | ↓↓ peak transient | ↓ | — |
| H2 ReadyToRun | ↓ warm-up JIT | ↓ JIT code | ↑ exe size (+~30%) | ↓↓ (20–40%) |
| H3 WMI over PowerShell | ↓ per read | ↓ transient | ↓ | ↓ page load |
| M1 pause backdrop | ↓ idle (backgrounded) | — | — | — |
| M2 no compression | ↓ decompress | — | ↓ (no temp extract) | ↓ |
| M3 lazy sensors | ↓ (non-Sensors users) | ↓ (driver+polling) | — | ↓ |

Net: the app's **memory floor is dominated by WPF + the bundled runtime** (~80–120 MB working set) and can't be
cut much without breaking the "no-install, self-contained" UX; the realistic wins are **startup time**
(H2/M2), **bulk-operation CPU/time** (H1), and **idle/background CPU** (M1) plus fewer process spawns (H3).

---

## Potential Risks and Trade-offs

- **H2 ReadyToRun / M2 no-compression:** larger exe/download. Pure size-vs-speed trade; no functional risk.
- **H1 batch removal:** a single failing app must not abort the batch — use `-EA SilentlyContinue` per item and
  per-name result reporting so the UI still shows which succeeded (parity with current behaviour).
- **H3 WMI reads:** WMI provider availability varies (another AV can hide Defender WMI) — keep the existing
  graceful "unavailable" fallback.
- **M1 backdrop pause:** ensure it re-hooks on `Activated` and settles a static frame, so users never see a
  frozen/black backdrop.
- **M3 lazy sensors:** first Sensors-page open becomes slightly slower (driver load moves there from startup) —
  desirable trade (most users never open it).

---

## Final Prioritized Action Plan (highest ROI → lowest)

1. **H1 — Batch bulk PowerShell removal** (Low–Med effort, big CPU/time/memory win on the operation users run).
2. **H2 — `PublishReadyToRun=true`** (Low effort, 20–40% faster cold start, less warm-up CPU/memory).
3. **M1 — Pause backdrop when unfocused/minimized** (Low–Med effort, cuts idle background CPU/GPU).
4. **H3 — WMI instead of PowerShell for Defender/Ransomware reads; `DnsFlushResolverCache` instead of `ipconfig`**
   (Med effort, fewer spawns, snappier pages).
5. **M2 — Turn off single-file compression** *if* startup latency > download size (Low effort, config choice).
6. **M3 — Verify/enforce lazy sensor-stack init + idle teardown** (Med effort, helps the majority who never
   open Sensors).
7. Low-impact L1–L5: mostly "leave as-is" confirmations (GC mode, no trimming, no InvariantGlobalization).

**Recommended first slice to implement:** H1 + H2 + M1 — all low-risk, independently shippable, test-gated, and
covering the three axes that actually move (bulk-op cost, startup, idle CPU).
