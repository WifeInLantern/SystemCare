# SystemCare UI/UX Redesign Spec

> A design review by a design-systems lens. **Verdict up front:** SystemCare already has a mature,
> CI-verified design system (v4 — see `docs/DESIGN-SYSTEM.md`). The right move is **refinement and
> compliance**, not a new visual language. This spec targets four real gaps: (1) recently-added pages that
> drifted off the system, (2) navigation that no longer scales to 48 tools, (3) specific accessibility gaps,
> (4) window-resize resilience. No functionality is removed; the neon "Night City" identity is preserved.

Platform note: SystemCare is a **WPF desktop app** (Windows 10/11, `MinWidth 960`). The brief's
"mobile/tablet" requirements don't literally apply — there is no mobile target. I translate "responsive"
honestly to **window-resize reflow** across the desktop size range, and flag the rest as N/A rather than
invent scope.

---

## 1. UI/UX Audit Summary

**Strengths (keep, don't touch):**
- **Tokenized system** in three load-ordered dictionaries (`Theme.xaml → Cyberpunk.xaml → Components.xaml`),
  with a `SmokeTest` that fails CI if a key is missing. This is better governance than most production apps.
- **Motion system** is exemplary: code-driven attached behaviors, a documented duration ramp, glow
  discipline (`GlowXl` = one per app), and a complete **reduce-motion contract** that freezes loops and snaps
  entrances live. Genuinely ahead of industry norm — I recommend **no changes** here.
- **Elevation/glass scale** (`Surface0–3` with rising alpha + rim light) gives real depth without clutter.

**Gaps (the redesign target):**
- **System drift.** The 15 tool pages added in 2.4.x–2.6.x bypass the type/color tokens with inline
  `FontSize`/`Opacity`/hex literals — the exact anti-patterns §9 of the design doc warns against. This is the
  single biggest, most fixable issue and it hits **both consistency and accessibility**.
- **Navigation at scale.** 48 nav items in one 180px scrolling pane. Discoverability and scannability degrade
  well before 48; there's no search, no favorites, no way to jump to a tool by name.
- **Accessibility.** Tertiary text tokens *pass* WCAG AA on their own, but pages that stack `Opacity="0.7"`
  on top of them drop **below** AA. Custom-drawn controls lack a visible keyboard-focus ring. Dark-only with
  no high-contrast accommodation.
- **Window-resize resilience.** New pages use fixed-pixel columns (110/220px) that don't reflow when the
  window is narrow.

---

## 2. Design Issues Identified (confirmed, with evidence)

| # | Severity | Issue | Evidence |
|---|---|---|---|
| D1 | High (consistency + a11y) | Inline `Opacity`/`FontSize` instead of type tokens | `DnsPage`, `SpeedTestPage`, `LargeFilesPage`, `ScheduledTasksPage`, `ContextMenuPage`, `BootAnalyzerPage` all use `Opacity="0.7/0.78"`, `FontSize="12/13"` — the doc's §9.2 says "captions = `TextCaption` (never `FontSize="12" Opacity="0.65"`)" |
| D2 | High (a11y) | Stacking opacity on already-dim text fails AA | `TextTertiaryColor #6E86A6` on `Surface0 #0A0E14` ≈ **5.1:1** (passes), but ×`Opacity 0.7` ≈ **3.5:1** (fails AA for <18px) |
| D3 | Medium (consistency) | Inline `#33445566` chip fills instead of `*SubtleBrush` | disabled-state chips in `ScheduledTasksPage`/`ContextMenuPage`/`BootAnalyzerPage` — doc §3 says "Never inline a `#33…` literal" |
| D4 | High (IA/usability) | 48 nav items, no search/favorites | `MainWindow.xaml` — one flat scrolling pane |
| D5 | Medium (a11y) | No visible keyboard-focus ring on custom controls | `HealthGauge`, chips, cards have no `FocusVisualStyle` |
| D6 | Medium (responsiveness) | Fixed-pixel columns don't reflow | `LargeFilesPage` (110px×2), `DnsPage` (220px) |
| D7 | Low (a11y accommodation) | Dark-only, no high-contrast mode | intentional identity, but no accommodation for low-vision / high-ambient-light users |

**Explicitly NOT issues** (reviewed, correct): motion system, reduce-motion handling, elevation scale, glow
tiers, the token architecture itself, iconography (consistent Fluent System Icons via WPF-UI).

---

## 3. Proposed Design System (refinements only — additive, no renames)

The system is sound; these are **small additions** that make compliance easy and close a11y gaps. Per the
doc's rule, new keys get added to `SmokeTest`.

**3.1 Text tokens — fill the gap that caused the drift.** Pages reached for inline opacity because the ramp
lacked a couple of everyday roles. Add:
- `TextBodyStrong` (13, `TextPrimaryColor`, SemiBold) — for the many `FontWeight="SemiBold"` labels.
- `TextMetricValue` (right-aligned numeric, `LabelFontFamily`) — for the size/speed/duration columns.
- Formalize that **no view sets `Opacity` on text** — emphasis comes only from the token (which is tuned to
  pass AA). This is the core rule that fixes D1+D2 at once.

**3.2 Focus visual (fixes D5).** Add a single app-wide `CyberFocusVisual` `FocusVisualStyle`: a 2px
`NeonCyanBrush` outline at `RadiusMd`, applied via an implicit style on `Control` and explicitly on the
custom controls. Keyboard users get a consistent, on-brand focus ring; mouse users are unaffected.

**3.3 High-contrast accommodation (fixes D7).** Mirror the proven **reduce-motion pattern**: a
`Settings ▸ High contrast` toggle that swaps an alternate token set — raise `TextSecondary/Tertiary`
luminance to ≥7:1, drop glow to `GlowSm` everywhere, thicken strokes. It reuses `ReduceMotionChanged`'s
live-toggle plumbing (`HighContrastChanged`). The default stays the neon identity; this is an *accommodation*,
not a second theme.

**3.4 Chip/state tokens.** No new tokens needed — D3 is fixed by *using* the existing `CyberChip*` +
`*SubtleBrush` instead of inline hex. A "neutral/disabled" chip is the one missing variant → add
`CyberChipNeutral` + `ChipTextNeutral` (replacing the ad-hoc `#33445566`).

---

## 4. Layout & Navigation Improvements

**4.1 Command palette (fixes D4 — the highest-value change).** A `Ctrl+K` overlay: a single search field over
`Surface3` glass that fuzzy-matches all 48 tools by name/category and Enters to navigate. This is the
standard, non-trendy answer to "too many nav items" (VS Code, Raycast, every pro tool). Reasoning: at 48
tools, *recognition* (scanning a list) is beaten by *recall + search*; a palette makes every tool reachable in
~2 keystrokes without touching the existing nav.

**4.2 Favorites / pinned tools.** A small "Pinned" group at the top of the nav (persisted in settings). Users
live in 3–5 tools; surfacing those removes most scrolling. Reasoning: respects the existing IA (Clean/Optimize/
Analyze/Protect stays) while cutting the daily path length.

**4.3 Collapsible category groups.** Let the four `NavigationViewItemHeader` groups collapse. Reduces the
resting height of a 48-item pane; remembers state. Low-risk, native-ish pattern.

**4.4 Keep the four-category IA.** Clean/Optimize/Analyze/Protect is a sound mental model — no re-architecture.
The problem is *volume within groups*, solved by 4.1–4.3, not by re-grouping.

---

## 5. Animation & Transition Improvements

The motion system needs **restraint, not expansion**. Recommendations:
- **No new animations.** The behavior catalog already covers entrances, hover, press, loading, count-up.
- **Audit for over-reach:** ensure each page honors "one breathing element per screen" and "GlowLg = max 1–2
  heroes." (e.g., verify `SpeedTestPage`'s `NeonPulse` Start button is the only breathing element there — it
  is.)
- **Command palette motion:** open = `FadeVisible` + 8px rise at `Motion.Base`; close = `Motion.Gentle`;
  results list = `StaggerChildren` capped at 400ms — all existing behaviors, reduce-motion-aware for free.
- **Keep** the `FadeInWithSlide` nav transition and its reduce-motion `None` swap (already correct).

Reasoning: the brief says "intentional and sparing." The system is already there; the risk is *adding*
motion, so the recommendation is discipline.

---

## 6. Accessibility Improvements

1. **Contrast (D1/D2):** remove all text `Opacity`; use tokens tuned to pass AA (≥4.5:1 small, ≥3:1 ≥18px).
   Re-verify tertiary captions land ≥4.5:1 with no multiplier.
2. **Focus visibility (D5):** app-wide `CyberFocusVisual` + custom-control focus rings; verify full
   keyboard traversal (Tab order, `IsTabStop` on interactive cards, Space/Enter activation).
3. **High-contrast accommodation (D7):** `Settings ▸ High contrast` (§3.3).
4. **Screen readers:** add `AutomationProperties.Name`/`HelpText` to icon-only buttons and custom controls
   (e.g., `HealthGauge` should announce "PC health 78 of 100, Good"). Currently icon buttons rely on
   tooltips, which narrators don't always read.
5. **Reduce motion:** already fully honored — **no change** (call it out as a strength).
6. **Target sizes:** ensure interactive rows/chips meet ≥ 24×24 effective hit area (most do via card padding).

---

## 7. Responsive (Window-Resize) Improvements

Honest scope: desktop only; **mobile/tablet are N/A**. Within the desktop range (`960 → maximised`):
- **Fluid columns (D6):** replace fixed `Width="110/220"` with `MinWidth` + `*` so tables/rows reflow; let the
  DNS adapter combo be `MinWidth="180"` star-weighted.
- **Wrap toolbars:** page action rows (Scan/Cancel/filters) in a `WrapPanel` so they stack instead of clipping
  at narrow widths.
- **Nav pane:** consider `OpenPaneLength` responsive to width, or icon-only collapse under a threshold (WPF-UI
  supports compact mode) — pairs well with the command palette (§4.1) so collapsing the pane loses nothing.
- **Verify at `MinWidth 960`** every page: no horizontal scrollbar, no clipped controls.

---

## 8. Component-Level Changes

| Component | Change | Why |
|---|---|---|
| Text (all pages) | Inline `Opacity`/`FontSize` → `TextCaption`/`TextBody`/`TextH3`/`TextBodyStrong` | D1/D2 consistency + contrast |
| Status chips | Inline `#33445566` → `CyberChipNeutral` | D3 |
| Cards/rows | Add `FocusVisualStyle`, `IsTabStop`, `AutomationProperties.Name` | D5, screen readers |
| Data rows (Large Files, etc.) | Fixed → fluid columns | D6 |
| Nav (`MainWindow`) | + command palette, + favorites, + collapsible groups | D4 |
| `HealthGauge` & custom controls | Focus ring + automation name | D5, a11y |
| Settings | + High contrast toggle (beside Reduce motion) | D7 |

No component is redesigned visually — they're brought **into** the existing system.

---

## 9. Screens Requiring Redesign

Honestly, **most screens need zero visual change** — they already comply. The work is concentrated:

- **Compliance sweep (visual-parity, token-only):** `DnsPage`, `SpeedTestPage`, `LargeFilesPage`,
  `ScheduledTasksPage`, `ContextMenuPage`, `BootAnalyzerPage`, `BrowserCleanupPage`, `HostsBlockerPage`,
  `BreachCheckPage`, `RansomwareShieldPage`, `DefenderPage`, `BatteryHealthPage` — replace inline styling with
  tokens. *Looks the same, measures better.*
- **New surface:** command palette overlay (additive).
- **Settings:** add High-contrast toggle + Favorites management.
- **`MainWindow`:** nav favorites + collapsible groups.
- **Untouched:** Dashboard, Auto Care, Cleanup, and every page already using the tokens — they're the
  reference the sweep brings the others up to.

---

## 10. Final Implementation Plan

Phased, lowest-risk first, each independently shippable and test-gated:

**Phase 1 — Compliance + accessibility pass (highest value / lowest risk).** Sweep the drifted pages to
tokens (D1/D2/D3), add `TextBodyStrong`/`TextMetricValue`/`CyberChipNeutral` + `SmokeTest` keys, add the
app-wide `CyberFocusVisual` (D5), add `AutomationProperties.Name` to icon buttons/custom controls. **Pure
visual-parity + measurable a11y gains, no functional change.** This also fixes drift I introduced.

**Phase 2 — Navigation at scale.** Command palette (`Ctrl+K`) + favorites/pinning + collapsible groups (D4).

**Phase 3 — Responsive reflow.** Fluid columns + wrapping toolbars + optional compact nav (D6).

**Phase 4 — High-contrast accommodation.** `Settings ▸ High contrast` mirroring the reduce-motion plumbing
(D7), with a contrast-audited alternate token set.

Each phase: implement → `SmokeTest` + unit build/test gate → verify reduce-motion both ways → ship as a minor
release. Validation checklist per screen: token-only styling, visible focus, keyboard-operable, ≥4.5:1 text,
no clipped layout at `MinWidth 960`, identical functionality.

---

### Design review sign-off

The redesign **improves** consistency (drift → tokens), accessibility (contrast, focus, screen readers,
high-contrast option), and window-resize resilience, **without** a teardown, new visual language, added motion,
or removed functionality — exactly what the constraints ask. The neon "Night City" identity and the excellent
motion/reduce-motion system are preserved as-is. The single highest-leverage change is **Phase 1**: it turns
the design system's own CI-enforced rules into reality on the pages that drifted, fixing consistency and
accessibility in one pass.
