# SystemCare — Code Audit & Optimization Review (2.19.3)

> Independent, evidence-based review of the whole program on 2026-07-20. Scope: 86 services
> (~12,800 lines), 52 view-models (~8,500), 53 XAML views, plus build/release infra. Every finding
> below cites concrete evidence from the source. Ordered by **value / risk**, not severity alone.

---

## 0. Headline

The app is well-architected and defensively coded — the bones are good. The highest-value work now
is NOT more features; it's removing three recurring sources of friction (build fragility, an
untested OS-mutating layer, and a 566-line composition root) and finishing two sweeps the original
PROGRAM-ANALYSIS.md opened and never closed (empty-catch logging, the O(N*M) leftover scan).

Nothing here is an emergency. But #1 and #2 have each cost real time this development cycle and will
keep doing so until fixed.

---

## 1. Build fragility from piecemeal usings — FIX FIRST

Evidence: three build breaks this cycle, same root cause:
- HostsBlockerService.cs — missing `using System.Net.Http;` (2.14)
- SmokeTest/Program.cs — missing `using System.IO;` AND `using System.Linq;` (2.19)
- SystemCare.csproj line 36 already carries `<Using Include="System.IO" />` — added reactively
  after an earlier break.

`ImplicitUsings=enable` is on, but the WPF SDK's implicit set is narrower than most expect (it does
not globally include System.Net.Http, and the picture drifts per project). Patching one namespace at
a time guarantees the next new file that needs HttpClient/File/Linq breaks the build again — and with
no local compile in this workflow, each break is a full round-trip.

Recommendation (Low effort, ~zero risk): add one src/SystemCare/GlobalUsings.cs with
`global using` for System.IO, System.Linq, System.Net.Http, System.Threading,
System.Threading.Tasks, System.Collections.Generic — same for tools/SmokeTest — and delete the
ad-hoc `<Using Include>`. This class of break disappears permanently.

---

## 2. ProcessRunner has no default timeout — hangs are possible

Evidence: Helpers/ProcessRunner.cs awaits WaitForExitAsync(ct) with no timeout of its own; 6 call
sites pass no CancellationToken (default None). A child that never exits (a wedged netsh, DISM,
chkdsk, or WSearch stop — the last one I had to hot-patch a 90 s timeout onto in 2.17) leaves the
await pending forever, silently wedging the calling operation.

Recommendation (Low/Low): give RunAsync a default max duration (e.g. 120 s) via an internal linked
CTS, so every external-tool call is bounded by construction. Removes a whole class of "spun forever"
reports and lets me drop the one-off SearchIndex timeout.

---

## 3. Output is unbounded in ProcessRunner

Evidence: the combined stdout+stderr StringBuilder grows without limit. DISM/SFC on a damaged store,
or a tool looping, can emit tens of MB — all held in memory, then handed to the UI console TextBox.

Recommendation (Low): cap the builder (e.g. 1 MB, keep the tail). The repair console already
truncates for display; make the capture itself bounded.

---

## 4. The OS-mutating layer is still largely untested — BIGGEST quality lever

Evidence: 331 test methods / 47 files — genuinely good for pure logic (scoring, parsers, formatters,
evaluators). But the services that CHANGE the system — registry cleaner, debloat, hosts writer,
powercfg/hibernation, search-index rebuild, driver/software update — are exercised only through mocks
of their own dependencies, never against a real (sandboxed) OS. This is W2 from PROGRAM-ANALYSIS and
remains the single highest-leverage quality investment.

Recommendation (Large, high value): a Windows Sandbox / CI job that runs the OS-mutating subset
against a throwaway VM and asserts reversibility (apply -> assert changed -> revert -> assert
restored). Start with the three highest-blast-radius tools: hosts blocker, registry cleaner, debloat.

---

## 5. Finish the empty-catch sweep (W3)

Evidence: 115 `catch { }` blocks with an empty body across services + view-models. Many are
legitimately best-effort (a sensor hiccup, an optional WMI class) — but 115 is a lot of blind spots,
and "best-effort" and "silently hiding a real failure" look identical in code. When something
misbehaves in the field, these are the places with no log line to explain it.

Recommendation (Medium, incremental): apply the "comment-or-log" rule — every empty catch gets a
one-line justifying comment OR a _log.Warn. A sweep, not a rewrite; do it per file when touching it,
and add a lint that flags a bare, uncommented empty catch in Services/.

---

## 6. Leftover scan sync-over-async (W4) — CORRECTED, low impact

Evidence: LeftoverScanService.OtherInstallLocations(app) (line 351) calls
apps.GetInstalledAppsAsync().GetAwaiter().GetResult() — a full re-enumeration of every installed app
— and it's invoked once per leftover candidate (line 111, inside the per-app scan). For N candidates
and M installed apps that's N*M enumerations, each blocking a thread-pool thread on sync-over-async.
It runs on a background scan thread (no UI deadlock) but is needless work.

CORRECTION (verified 2.19.4): CaptureCandidates is called ONCE per uninstall
(SoftwareUninstallerViewModel line 136), not in a loop over apps — so this is O(M) once, not
O(N*M). The original W4 framing (and my restatement of it) overstated the impact. What remains is
a single sync-over-async call on a background thread, once per uninstall: harmless in practice.

Recommendation: NO CHANGE. Fixing it would mean making CaptureCandidates async through the
interface for no measurable gain. Left as-is deliberately.

---

## 7. Split the composition root out of App.xaml.cs (W12)

Evidence: App.xaml.cs is 566 lines doing DI registration (~110 lines), startup orchestration, CLI
verb handling, single-instance mutex, splash, the update flow, and four global fault handlers. It's
the busiest file and the one most likely to collide on edits.

Recommendation (Medium, mechanical): move ConfigureServices into ServiceRegistration.cs and the
CLI-verb dispatch into CommandLineEntry.cs. No behavior change; a smaller, safer startup file.

---

## 8. Startup does a growing pile of fire-and-forget work

Evidence: cold start now kicks off back-to-back: Autorun Guard diff, boot-history read, monthly
report check, restore-point watchdog, plus a new 6 h timer — each hitting disk/WMI/registry.
Individually cheap; collectively they contend with the first frame the user waits for.

Recommendation (Low): sequence them behind a single low-priority post-idle dispatcher hook (after
the window is shown + a short delay). The 2.11 backdrop-pause work shows the same instinct.

---

## 9. ConfigureAwait(false) absent in the service layer (W10)

Evidence: 0 uses across Services/*.cs. Every service-layer await needlessly marshals its
continuation back to the UI context.

Recommendation (Low, optional): add .ConfigureAwait(false) in the pure-service layer (not
view-models). Small UI-thread-churn win; lowest-value item here — skip if the diff noise isn't worth it.

---

## 10. Already right — don't touch

- HttpClient lifetimes: BreachCheckService and SpeedTestService use static shared clients (correct).
- The .Result uses in AutoCareService/DashboardViewModel are each preceded by await Task.WhenAll(...)
  — the tasks are already complete; safe, leave them.
- Design-system governance (3-dictionary + SmokeTest, now with the contrast lint) is an asset.
- Reduce-motion contract and glow discipline remain best-in-class.

---

## 11. Suggested sequencing

- 2.20 "hardening": #1 GlobalUsings, #2 ProcessRunner timeout, #3 output cap. (#6 dropped — see correction.)
  ~1-2 days, all low-risk, all remove recurring friction/bugs.
- 2.21 "structure": #7 split App.xaml.cs, #8 startup sequencing, #5 empty-catch lint + first sweep.
- Ongoing: #4 sandbox integration tests — the real quality ceiling; start with hosts/registry/debloat.
- Optional: #9 ConfigureAwait, only if touching the files anyway.

## 12. Process note (meta, but it's costing time)

Two things outside the code slow every cycle: (1) no local compile in this workflow, so type-level
mistakes (the missing-using breaks) only surface when you run the release script — a `dotnet build`
gate before packaging catches them in seconds; (2) the security-guidance plugin hook is misconfigured
on this machine and intermittently blocks the file-edit tools (I fall back to shell edits). Fixing
both makes every future change land faster.
