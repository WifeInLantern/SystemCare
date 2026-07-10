# SystemCare — Full Program Analysis

_Analysis date: 2026-07-08 · against `main` @ v2.12.0 · net8.0-windows WPF_

This is a whole-program audit: architecture, robustness, security/blast-radius, concurrency,
performance, testing, distribution, accessibility, and maintainability. Every claim below is grounded
in the current source, and each weak point comes with a concrete, proportionate improvement. A short
"what not to touch" section at the end guards against churning things that already work.

---

## 1. Snapshot

| Metric | Value |
|---|---|
| Source files / LOC | 484 files · ~42,200 LOC |
| Test files / LOC | 52 files · ~5,050 LOC (~12% of source) |
| Services · ViewModels · Views | 76 · 48 · 57 |
| `try` blocks · empty `catch` | 401 · 125 |
| `Task.Run` · `lock` · `async void` | 85 · 15 · 25 |
| `.Result`/`.Wait()`/`GetResult()` | 11 |
| Registry writes · `Process.Start` · P/Invoke | 83 · 19 · 17 |
| PowerShell spawns | 9 (down from ~14 pre-2.11) |
| TODO/HACK/FIXME · NotImplemented | 0 · 0 |

The codebase is large, disciplined (no TODO/HACK debt markers, heavy defensive `try/catch`), and clearly
maintained. The issues below are refinements on a fundamentally sound app, not a rescue.

---

## 2. Strengths (worth preserving)

- **Crash safety is real.** `App.xaml.cs` wires all three .NET fault channels —
  `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, and
  `TaskScheduler.UnobservedTaskException` — so unobserved failures (including from `async void`
  navigation) land in a handler rather than killing the process silently.
- **Reversibility is better than it looks.** There is a genuine Windows **System Restore** integration
  (`RestorePointService` via the `SystemRestore` WMI class), a `BackupConfirmationService` policy gate,
  the Game Booster **crash-safe rollback journal**, and per-item **Revert** in `DebloatService`.
- **Secrets are handled correctly.** The GitHub update token is DPAPI-encrypted at rest, with a
  one-time migration that rewrites any legacy plaintext token into the protected field.
- **Update path is defensive.** HTTPS-only downloads, `.part` staging + atomic rename, SHA-256
  verification, token only sent to GitHub hosts, and never launching a non-`.exe` asset as an installer.
- **Design-system discipline.** Three-layer resource dictionaries with a CI **smoke test** (82 token/
  style keys verified every build) — a genuinely unusual and valuable guardrail.
- **Pure-logic test coverage is solid** where it exists: parsers (winget), scoring, formatters, and
  view-model logic are well covered (490+ tests).

---

## 3. Weak points (prioritized)

### High

**W1 — Distribution is the weakest link (unsigned + unpublished).**
The app requires administrator and rewrites the hosts file, registry (83 sites), services, and firewall,
yet ships **unsigned** — SmartScreen shows "unknown publisher." Separately, **no GitHub Release has ever
been published**, so `UpdateService` (which correctly treats a 404 as "no update") never sees a payload:
the in-app updater is effectively inert for every existing user. _Fix:_ acquire a code-signing
certificate and set `SYSTEMCARE_PFX`/`SYSTEMCARE_PFX_PASSWORD` (the pipeline now signs when present);
publish the Release pages so the updater comes alive. This is the single highest-value work item and
needs no new code.

**W2 — Test coverage is concentrated away from the risk.**
~12% test-LOC ratio, and the ~76 services that actually *mutate the system* (registry, services, netsh,
P/Invoke) have effectively no automated coverage — the tests cover pure logic. The blast radius and the
test coverage are inversely correlated. _Fix:_ stand up an integration test project that runs the
*read-only/idempotent* subset in a disposable environment (Windows Sandbox `.wsb`, or a
`windows-latest` GitHub Actions runner) and snapshots registry/service state before/after. Even covering
20 high-risk services would move the needle.

**W3 — Swallowed exceptions blunt diagnosability.**
125 empty `catch` blocks remain. Many are legitimately best-effort (and carry justifying comments), but
the pattern hides real failures across services, and the app has a perfectly good `ILogService` that
only 24 of 76 services use. _Fix:_ adopt a rule — a `catch` may swallow **only** with a one-line comment
justifying it; everything else logs at Warn/Error. Sweep service-by-service (AppPackageService was done
in 2.12.0 as the template).

### Medium

**W4 — `LeftoverScanService.OtherInstallLocations` is sync-over-async and O(n²).**
It calls `apps.GetInstalledAppsAsync().GetAwaiter().GetResult()` **inside a per-app method**, blocking a
thread and re-enumerating the entire installed-apps list on every invocation. On a machine with many
apps this is both a latency and a deadlock-shape risk. _Fix:_ fetch the installed-apps list **once**,
pass it in, and make the method synchronous over that cached list.

**W5 — NuGet versions straddle two .NET major lines.**
The target is `net8.0-windows`, but `Microsoft.Extensions.DependencyInjection 10.0.9` and
`System.Management 10.0.2` are .NET 10 packages, mixed with 8.0.x for others. This "works" via
compatibility shims but is off the supported matrix and can drag in newer transitive dependencies. _Fix:_
pin everything to the 8.0.x LTS line, **or** deliberately move the whole app to net10 — don't straddle.

**W6 — Two independent bloatware-removal code paths.**
`AppPackageService` (AppX removal, curated `Bloat[]`) and `DebloatService` (`BloatApps[]` + registry/task
tweaks) both encode "what is bloat" and "how to remove it." They can and will drift. _Fix:_ extract a
single source of truth for the bloat catalog and share the removal primitive.

**W7 — System Restore is opt-in, not enforced for destructive batches.**
The restore-point machinery exists but is gated by a `CreateRestorePoint` setting / confirmation. For the
highest-risk *batch* operations (mass debloat, registry clean, service changes), a restore point should
be created **automatically and unconditionally**, independent of the user's routine-cleanup preference.
_Fix:_ make a restore point (or a scoped registry export) mandatory in the execute path of the few
genuinely destructive batch commands.

**W8 — Accessibility is absent.**
**Zero** of 57 XAML views set `AutomationProperties` (name/help text). Custom-drawn controls and icon-only
buttons are invisible to screen readers and UI automation, and to your own future UI tests. _Fix:_ add
`AutomationProperties.Name` to icon buttons and key controls; it also unlocks automated UI testing.

### Lower

- **W9 — `async void` navigation (23 `OnNavigatedTo`).** The global handlers catch faults, but each
  should still self-contain a `try/catch` so a page-load failure degrades to an inline error instead of a
  global dialog. One already does; make it the standard.
- **W10 — No `ConfigureAwait(false)` in services.** Minor for an app, but service-layer awaits needlessly
  resume on the UI context. Adding it in the pure-service layer trims UI-thread churn.
- **W11 — English-only (0 `.resx`).** All strings are hardcoded. Only worth addressing if you want reach
  beyond English; if so, extract to resources now while the surface is known.
- **W12 — A few large files.** `Helpers/Animations.cs` (792), `App.xaml.cs` (485, doing DI + startup +
  fault handlers), `SettingsViewModel` (439). Not alarming; `App.xaml.cs` is the best split candidate
  (move the DI composition root into its own `ServiceRegistration` class).
- **W13 — Uncompressed single-file exe (2.11+).** The startup win traded size; the on-disk exe is now
  large. Fine, but if size becomes a complaint, make compression a per-channel choice (compressed for the
  download, uncompressed for a "fast-start" build).

---

## 4. Suggested improvements — roadmap

**Quick wins (hours, low risk)**
1. Publish the GitHub Release for v2.12.0 → the updater goes live (W1).
2. Fix `LeftoverScanService` O(n²) sync-over-async (W4).
3. Pin NuGet packages to a single .NET line (W5).
4. Add `AutomationProperties.Name` to icon-only buttons on the top 5 pages (W8, start).

**Medium (days)**
5. Continue the swallowed-`catch` sweep with the comment-or-log rule (W3).
6. Make a restore point mandatory in destructive batch execute paths (W7).
7. Consolidate the two bloat catalogs/paths behind one abstraction (W6).
8. Split `App.xaml.cs` composition root out (W12).

**Larger bets (worth planning)**
9. Integration test harness in Windows Sandbox / CI for the OS-mutating subset (W2) — the highest-leverage
   quality investment.
10. Code-signing pipeline end-to-end once a certificate is in hand (W1).
11. Accessibility pass across all views, then a smoke-level automated UI test using the automation names
   you just added (W8 → W2).
12. Localization extraction to `.resx` if international reach is a goal (W11).

---

## 5. What NOT to change (avoid churn)

- **The `.Result` uses in `AutoCareService`/`DashboardViewModel` are safe** — each is preceded by
  `await Task.WhenAll(...)`, so the tasks are already complete; leave them.
- **The `Opacity`-on-bright-text convention** passes WCAG AA and is app-wide; don't do a blanket purge.
- **The broad `Protected[]` allowlist** in `AppPackageService` is intentionally over-inclusive — being
  broad there is the *safe* direction; keep it loose.
- **Best-effort `catch` blocks with justifying comments** (e.g. "not installed — Start=4 applies on
  reboot") are correct as-is; only the *un-commented, failure-hiding* ones need logging.
- **The design-system smoke test and three-dictionary structure** are an asset — extend, don't refactor.

---

_Net: SystemCare is a well-architected, defensively-coded app whose biggest gaps are outside the code —
signing and release publishing — followed by a test suite that doesn't reach the risky OS-mutating layer.
Address W1 and W2 and the program moves from "good" to "trustworthy at scale."_
