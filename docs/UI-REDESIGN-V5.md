# SystemCare UI/UX Redesign — v5 "Night City, Refined"

> **Supersedes `docs/UI-REDESIGN.md`.** Based on an independent audit of the v2.12 codebase
> (49 pages, 3 style dictionaries, 8 custom controls) on 2026-07-10. Every claim below is
> verified against source; contrast ratios are computed, not estimated.
>
> Companion artifact: `docs/mockups/ui-v5-mockup.html` — interactive before/after mockups of
> the navigation shell, Dashboard, and a representative tool page.

---

## 1. Executive Summary

SystemCare does not need a new visual language. It needs its existing one **enforced, scaled,
and made accessible**. The v4 design system is genuinely strong — tokenized palette, CI-verified
keys, an elevation/glass scale, and a reduce-motion-aware motion system that is ahead of industry
norm. The premium feel the brief asks for is being lost in three places:

1. **The system exists but the pages don't use it.** ~360 inline `FontSize` and ~250 inline
   `Opacity` overrides across the 49 view files — including `DashboardPage.xaml` itself — bypass
   the documented type ramp (`TextH2/H3/Body/Caption`). This is the single largest source of
   visual inconsistency *and* of accessibility failures (stacked opacity silently drops text
   below WCAG AA).
2. **Navigation hasn't scaled.** 47 items in one flat 180px pane, with icon collisions (Globe24
   used by 3 tools, the Shield family by 6, Broom24 by 2). A Ctrl+K command palette already
   exists but has **no visible affordance anywhere** — the app's best navigation feature is a
   secret.
3. **Accessibility is one tier below the visual quality.** Zero `AutomationProperties` in the
   app, custom-drawn controls (HealthGauge, Treemap, charts) are not keyboard-focusable and
   invisible to screen readers, disabled text sits at 2.14:1, and there is no high-contrast
   accommodation.

The v5 strategy is therefore: **evolve, don't replace**. Keep the identity anchors (cyan #00E5FF /
magenta #FF2A6D on near-black, Orbitron/Rajdhani, glass + rim light, angular 4px corners). Rebuild
the type ramp with real size contrast, outlaw opacity-as-hierarchy, restructure navigation around
the four existing categories plus pinned/recent tools and a *visible* search, make the health score
explainable on the Dashboard, and close the accessibility gap. Enforcement is automated: the
existing `SmokeTest` CI gains lint rules so drift can never re-accumulate.

**Expected outcome:** a visibly calmer, more legible, more premium app with the same personality;
every text element ≥ 4.5:1; a nav that scales to 60+ tools; and a design system that is
self-defending.

---

## 2. Key Problems Identified (verified, with evidence)

| # | Severity | Problem | Verified evidence |
|---|----------|---------|-------------------|
| P1 | **High** — consistency | Type ramp bypassed app-wide. Inline `FontSize` used ~360× (111× `"11"`, 106× `"12"`, 53× `"13"` — exactly the sizes `TextCaption`/`TextH3`/`TextBody` exist to own) | grep across `Views/*.xaml`; even `DashboardPage.xaml` (quick-tile captions) uses `FontSize="11" Opacity="0.7"` |
| P2 | **High** — accessibility | Opacity-as-hierarchy fails AA. `TextPrimary × 0.5` → 4.94:1 (borderline); **`TextSecondary #8FA6C0 × 0.7` → 4.28:1 (fails AA <18px)**; `× 0.6` → 3.45:1; `× 0.55` → 3.10:1; `× 0.5` → 2.74:1. There are 87 instances at ≤ 0.6 | computed sRGB contrast vs `Surface0 #0A0E14`; 49 of 49 view files contain inline text opacity |
| P3 | **High** — IA / usability | 47 flat nav items in a 180px pane; no favorites, no recents, no visible search. The Ctrl+K `CommandPalette` exists (`Controls/CommandPalette.xaml`) but is reachable **only** by knowing the hotkey | `MainWindow.xaml` lines 46–290; `MainWindow.xaml.cs:52-61` (hotkey only) |
| P4 | **Medium** — IA / scannability | Icon collisions destroy glyph-based scanning: `Globe24` ×3 (Browser Cleanup, Network, Secure DNS), `Shield*` family ×6, `Broom24` ×2 (Cleanup, Debloat), `ShieldKeyhole24` ×2 (Ransomware Shield, Breach Checker) | `MainWindow.xaml` |
| P5 | **High** — accessibility | Zero `AutomationProperties` anywhere; `HealthGauge`, `TreemapControl`, `SparklineChart`, `BarChart` have no `Focusable`, no `AutomationPeer` — the app's core value (the score) is invisible to assistive tech | grep `AutomationProperties` → 0 hits; `Controls/*.cs` → no peers |
| P6 | **Medium** — accessibility | Disabled text `#3A4A60` = **2.14:1**; no visible focus ring on custom-drawn controls (`CyberFocusVisual` exists in `Components.xaml:205` but is applied in only one style) | `Cyberpunk.xaml:64`; computed |
| P7 | **Medium** — responsiveness | Hardcoded `UniformGrid Columns="5/3/2"` and fixed-pixel columns (`LargeFilesPage` 110px ×2, `DnsPage` MaxWidth 260) don't reflow between MinWidth 960 and large displays | `DashboardPage.xaml:73,112,144`; `LargeFilesPage.xaml:88-89` |
| P8 | **Low** — accommodation | Dark-only with no response to Windows High Contrast or "reduce transparency"; glass alphas + animated backdrop can be illegible in high ambient light | by design, but zero accommodation path |
| P9 | **Low** — consistency | Radius story split: `CardRadius 4` (identity) vs `RadiusMd 8` vs `ControlCornerRadius 4` with no documented rule for when 8 applies | `Theme.xaml:92-94`, `Cyberpunk.xaml:71-72` |
| P10 | **Low** — clarity | Health score is a black box: one number, no breakdown of *why* it's 72 or what "Fix all" will actually do before it does it | `DashboardPage.xaml` hero region |

What the audit **cleared** (previously suspected, now verified fine): inline `#33…` hex chip fills
have already been eliminated (0 hits); the motion system, glow discipline, elevation scale, and
reduce-motion contract need **no changes** — they are the strongest part of the product.

---

## 3. New Design Vision

**"Night City, Refined."** The neon identity stays; the noise goes. Premium in 2026 dark-UI terms
(the standard set by Linear, Vercel, Arc, Raycast) is not more glow — it is *contrast discipline*:
very dark, slightly warm-black floors; text that is unapologetically legible; one saturated accent
doing focused work; depth from layered translucency and 1px rim light rather than shadows. v4
already has the floor, the glass, and the rim. v5 adds the discipline:

- **Hierarchy through type, never through dimming.** Three text colors (primary/secondary/tertiary),
  a ramp with real size contrast, and an outright ban on `Opacity` applied to text.
- **The accent is a signal, not a paint.** Cyan means "interactive or attention" — focus rings,
  primary actions, live data. Magenta means danger/heat only. When everything glows, nothing does;
  the glow laws (Sm/Md/Lg/Xl) already encode this — v5 makes pages obey them.
- **Navigation as a map, not a list.** The four categories (Clean/Optimize/Analyze/Protect) become
  the primary wayfinding unit; individual tools are reached through category hubs, pins, recents,
  or search — the same model Raycast and VS Code use for large command surfaces.
- **Explainable numbers.** Every score, gauge, and "Fix all" shows its inputs. Trust is the premium
  feature of a system-maintenance app.
- **Accessible by contract.** AA contrast, focus visibility, and automation names are CI-checked
  token laws, not aspirations.

---

## 4. Design System Specification (v5 deltas over v4)

The three-dictionary architecture (`Theme.xaml → Cyberpunk.xaml → Components.xaml`) and the
"never rename or remove a key" rule stay. v5 is additive plus one value change per rationale below.

### 4.1 Color palette

Identity anchors unchanged: `AccentColor #00E5FF`, `SecondaryColor #FF2A6D`, `VioletColor #B14CFF`,
`SuccessColor #00FFA3`, `WarningColor #FFD300`, surfaces `Surface0–3`, `GlassRimBrush`.

| Change | From → To | Rationale |
|---|---|---|
| `TextFillColorDisabledBrush` | `#3A4A60` (2.14:1) → **`#5A6E8C`** (≈3.6:1) | WCAG exempts disabled text, but 2.14:1 is *unreadably* disabled. 3.6:1 still reads as inactive while staying legible. |
| New `TextQuartenaryBrush` | **`#586C88`** (≈3.2:1) | A legitimate "de-emphasized non-essential" tier (watermarks, ghost placeholders ≥ 18px only). Exists so pages stop reaching for `Opacity="0.5"`. |
| New law | **Text opacity ban** | `Opacity` on `TextBlock`/`Run` is forbidden in Views. Hierarchy = the four text brushes. CI-enforced (§4.10). |
| New law | Accent semantics | Cyan = interactive/live. Magenta = destructive/danger only (never decoration). Violet = charts/tertiary data series only. Success/warning = status only. |

### 4.2 Typography hierarchy

Current ramp is compressed (H2 15 / H3 13 / Body 13 / Caption 11) — a 4px total spread produces
"everything looks the same size", which is why pages kept inventing 20/22/24/32 inline. v5 widens
the ramp and owns the large sizes pages actually need:

| Style (key) | Font / size / weight | Color | Use |
|---|---|---|---|
| `CyberDisplayText` | Orbitron Bold 30 + glow | primary | Hero wordmarks only (unchanged) |
| `TextMetricHero` **(new)** | Rajdhani SemiBold **32**, tabular | primary | The big live numbers (CPU %, RAM) — replaces inline `FontSize="32" FontWeight="Bold"` |
| `CyberPageTitle` | Rajdhani SemiBold 26 + soft glow | primary | Page H1 (unchanged) |
| `TextH2` | **17** SemiBold (was 15) | primary | Card/group titles |
| `CyberSectionHeader` | Orbitron SemiBold 15, cyan | accent | Section headers (unchanged) |
| `TextH3` | **14** SemiBold (was 13) | primary | Sub-sections |
| `TextBody` | **14** (was 13) | secondary | Body copy. 13px Rajdhani-adjacent text at typical 100% DPI is below comfortable reading size. |
| `TextBodyStrong` | 14 SemiBold | primary | Emphasis inside body |
| `TextCaption` | **12** (was 11) | tertiary | Hints, descriptions. 11px is below the practical floor for a thin humanist face; tertiary `#6E86A6` = 5.18:1 passes AA at 12px normal weight. |
| `TextMono` **(new)** | Consolas 12 | `ConsoleForegroundBrush` | Paths, hashes, console excerpts inline |

Line-height: set `LineHeight` = size × 1.4 on Body/Caption styles (WPF default is tighter;
wrapped two-line captions currently look cramped). Numerals in metrics use
`FontNumberSubstitution`-safe tabular figures so count-up animations don't jitter width.

### 4.3 Spacing system

The 4px scale (`SpaceXs 4 … Space2Xl 32`, `PagePadding 24`) is correct — no changes. One addition:
`Space3Xl 48` for hero-to-content separation on hub pages. New law: margins between sibling cards
come from the scale only (audit found ad-hoc `Margin="0,28,0,12"` — snap to 24/32).

### 4.4 Grid system

- Page content max-width **1100** (already on Dashboard) becomes a token `ContentMaxWidth` applied
  to every scrolling page — long-line reading and centered composition on wide monitors.
- Replace hardcoded `UniformGrid Columns="n"` with a new `AdaptiveGrid` attached behavior:
  `helpers:Layout.AdaptiveColumns="MinItemWidth=220"` computes columns from actual width.
  Breakpoints in practice: 960–1150 → 2-up stat cards / 3-up tiles; 1151–1500 → as today;
  >1500 → cap at current counts + wider gutters (don't stretch cards past ~420px).
- Fixed-pixel data columns (110/220px) become `Auto` + `SharedSizeGroup` with `MinWidth`, so long
  byte-strings and dates never clip.

### 4.5 Iconography

Rule: **one glyph = one tool.** Reassignments (all exist in the Fluent set already bundled):

| Tool | Was | Becomes |
|---|---|---|
| Browser Cleanup | Globe24 (×3 clash) | `GlobeClock24` |
| Secure DNS | Globe24 | `LockShield24` |
| Network Tools | Globe24 | `Router24` |
| Debloat | Broom24 (clash w/ Cleanup) | `BoxDismiss24` |
| Breach Checker | ShieldKeyhole24 (clash) | `Password24` |
| Security Audit | Shield24 (clash w/ Privacy) | `ShieldGlobe24` |

Sizes: 24px nav/actions, 20px in-card, 16px inline chips. Icon-only buttons **must** carry
`AutomationProperties.Name` + tooltip (CI rule §4.10).

### 4.6 Buttons

Existing trio (`CyberPrimaryButton` / `CyberGhostButton` / `CyberDangerButton`) + implicit
PressScale stays. Additions:

- **Hierarchy law:** max one Primary per view region; destructive actions always `CyberDangerButton`
  + confirmation for irreversible ops (File Shredder already does this — generalize).
- `ButtonBusy` pattern: primary buttons bound to a running command swap label → `TaskProgress`
  spinner inline, stay same width (no layout jump), disable siblings. Today several pages disable
  the button but give no in-button feedback.
- Min touch/click target 36px height (several toolbars use compact 28–30px buttons).

### 4.7 Forms & inputs

- Inputs sit on `Surface2`, focus = 1px cyan stroke + `GlowSm` (already the pattern; codify as
  implicit `TextBox`/`ComboBox` styles so pages can't diverge).
- Every input gets a visible 12px caption label above (never placeholder-as-label — placeholder
  disappears on type, which fails recall and screen readers).
- Validation: inline `DangerSoftBrush` caption under the field + `DangerSubtleStroke` on the field.
  New `CyberFieldError` composition documented in Components.

### 4.8 Cards

`ui:Card` (Surface1 + rim + hover glow) unchanged. Additions:

- **`CyberToolCard` (new)** for category hubs: 20px icon tile on `AccentSubtleBrush`, `TextH3`
  name, `TextCaption` one-liner, optional status chip (e.g. "2.1 GB found", "3 updates"),
  full-card hit target, `HoverLift`. This is the workhorse of the new IA.
- **`CyberStatCard` (new)**: icon + label header, `TextMetricHero` value, sparkline slot,
  progress slot — extracts the Dashboard stat-card recipe so Sensors/Benchmark/Battery pages stop
  rebuilding it with inline sizes.

### 4.9 Navigation patterns

See §5.1 for the full shell redesign. Pattern inventory: left pane (wayfinding) → category hubs
(browse) → command palette (teleport) → pinned+recents (habit). Breadcrumb `Category / Tool` under
the title bar on tool pages restores "where am I" that the flat list never provided.

### 4.10 Feedback states (standardized state machine)

Every tool page implements the same five states — audit shows each page currently improvises:

| State | Recipe |
|---|---|
| **Idle/empty** | Existing EmptyState composition (icon 40 → `EmptyStateTitle` → `EmptyStateHint` → ghost CTA) |
| **Working** | `TaskProgress` + skeleton swap via `FadeVisible` (existing recipe) + `ButtonBusy` |
| **Results** | List/table + summary chip row (count, size, severity) |
| **Error** | **`CyberBannerDanger` (new)**: `DangerSubtleBrush` fill, `DangerSubtleStroke`, icon, `DangerSoftBrush` text, retry ghost button. Currently errors go to snackbars that vanish — persistent errors need persistent surfaces. |
| **Success** | Snackbar (transient) + result chip (persistent), one-shot success sweep (§8) |

**CI enforcement (the self-defending part):** extend `tools/SmokeTest` with three lint rules that
fail the build: (1) `Opacity="0.` on any `TextBlock`/`Run` in `Views/`; (2) `FontSize="` in
`Views/` (styles only may set size); (3) icon-only `ui:Button` without `AutomationProperties.Name`.
Drift stops being a review burden.

---

## 5. Screen-by-Screen Redesign Recommendations

### 5.1 Navigation shell (`MainWindow.xaml`) — the highest-leverage change

**Before:** 47 items + 4 headers in one flat scrolling 180px pane (Clean 8 / Optimize 12 /
Analyze 13 / Protect 12 + Dashboard, Auto Care, Settings); hidden Ctrl+K palette; icon collisions;
no personalization.

**After — hub-and-spoke:**

- Pane (200px) shrinks to ~9 items: **Dashboard**, **Auto Care**, a **Pinned** group (user's
  starred tools, default: Cleanup, Boost, Startup), then **Clean / Optimize / Analyze / Protect**
  as four category items, **Settings** in footer.
- Each category item opens a **category hub page**: header (name, one-line promise, aggregate chip
  e.g. "4.3 GB reclaimable"), then a responsive grid of `CyberToolCard`s. Cards show live status
  chips where cheap to compute (junk size from last scan, update counts, security posture).
- **Search made visible:** a search affordance in the title bar — `⌕ Search tools   Ctrl+K` —
  opening the *existing* palette. Palette gains: match highlighting, "Recent" section when query
  is empty, and pin/unpin via right-click or a star in the row.
- **Recents:** last 3 visited tools auto-listed under Pinned (persisted in `AppSettings`).
- Tool pages get a breadcrumb row: `Optimize / Driver Updater`.

**Why:** recognition over recall works only when the list is scannable; at 43 items users scroll
and re-read. Categories are already the mental model (the headers exist; the README markets them).
Hub cards add the one thing a nav row can't: *status*, which turns navigation into triage —
the user goes where the numbers say to go. Frequency data (pins/recents) serves the 90% case of
returning to the same 4 tools. The palette serves power users; making it visible serves everyone
else (feature discovery via UI, not documentation).

*Migration safety:* keep `NavigationView` and all `TargetPageType` wiring; hubs are new pages;
the pane just gets fewer items. Old deep-links (tray, quick actions) unaffected.

### 5.2 Dashboard

**Before:** strong hero (gauge + Scan/Fix all), stat cards, drive cards, 5 quick tiles — but
captions at 11px/0.7 opacity, hardcoded 5-column tiles, and an unexplained score.

**After:**

- **Explainable score:** under the gauge, a horizontal chip row of score inputs: `Junk 2.1 GB ▼`,
  `Startup 9 items`, `RAM 78%`, `Security 4/5` — each a `CyberChip*` colored by contribution,
  each clickable → jumps to the tool. "Fix all" gets a hover/pre-flight tooltip listing exactly
  what it will do (clean junk + trim RAM + re-score). *Rationale: a one-number black box reads as
  snake oil to exactly the technical audience this app courts; explainability is trust.*
- Stat cards → `CyberStatCard` (tokens replace `FontSize="32"` inline); values use tabular figures.
- Quick tiles: replace with the user's **Pinned** tools (same personalization as nav — one mental
  model), `AdaptiveColumns` instead of `Columns="5"`.
- All `FontSize="11" Opacity="0.7"` captions → `TextCaption`.
- Keep: gauge + `GlowXl` (the app's one loudest element), count-ups, sparklines, stagger — this
  hero is already premium.

### 5.3 Category hub pages (new — 4 pages)

Composition: `CyberPageTitle` + promise line (`TextBody`) + aggregate status chip; grid of
`CyberToolCard`s (AdaptiveColumns, MinItemWidth 240); footer link "Run all safe actions in this
category" where meaningful (Clean). Stagger entrance; one `GlowLg` max (the aggregate chip).

### 5.4 Cleanup (representative scan-tool template)

The scan pages (Cleanup, Privacy, Registry, Duplicates, Empty Folders, Browser Cleanup, Deep
Cleanup) share a lifecycle; v5 formalizes the **Scan Page Template**: header row (title +
last-scan `TextCaption` + primary Scan button with `ButtonBusy`) → category checklist with
per-item size chips → sticky summary footer (`Surface3`): "3,412 files · 2.1 GB selected" +
`Clean` primary + `CyberDangerButton` only where deletion is irreversible. States per §4.10.
*Rationale: seven pages, one muscle memory; sticky summary keeps consequence visible at the
moment of commitment (the Gestalt of action + effect).*

### 5.5 Large Files / DNS / Scheduled Tasks / Boot Analyzer (the drifted cohort)

Mechanical but high-impact: inline sizes/opacities → tokens (P1/P2); fixed 110px columns →
`Auto` + `SharedSizeGroup` + `MinWidth` (P7); ad-hoc dim badges → `CyberChipNeutral`. Large
Files additionally gets its size column right-aligned with tabular figures (scanning a size
column is the page's whole job).

### 5.6 Settings

Group into cards by concern (Appearance & Motion / Maintenance / Notifications / Advanced);
every toggle gets a `TextCaption` consequence line ("Reduce motion: freezes all looping
animations and entrance effects"). Add the two new accessibility toggles from §7 (High-contrast
accommodation, Reduce transparency).

### 5.7 Command palette

Keep behavior; add: match-substring highlighting (cyan `Run`), empty-query state = Recents +
Pinned, a footer hint row (`↑↓ navigate · ↵ open · esc close`), and the title-bar entry point.

---

## 6. UX Improvements (cross-cutting)

1. **Triage-first information scent** — status chips on hub cards and score-input chips mean the
   UI tells users *where the problems are* before a single click (progressive disclosure of the
   25-tool surface).
2. **Consequence preview** — Fix all tooltip, sticky selection summaries, and confirmation copy
   that states counts/sizes ("Delete 3,412 files (2.1 GB) to Recycle Bin") — reversibility is the
   app's differentiator; the UI should sell it at every commit point.
3. **One template per page family** — scan pages, monitor pages (Sensors/Net Monitor/System Info),
   and manager pages (Startup/Services/Uninstaller) each get a documented layout recipe, so the
   40+ tools feel like one product, not a suite of plugins.
4. **Keyboard path everywhere** — Ctrl+K (existing) + visible affordance; Esc closes overlays
   (existing); add Ctrl+1..4 to jump to category hubs.

---

## 7. Accessibility Improvements

| Item | Action |
|---|---|
| Contrast | Text-opacity ban + token remap (§4.1–4.2) lifts every text element to ≥ 4.5:1 (computed floor after remap: tertiary 5.18:1 at 12px). Disabled → 3.6:1. |
| Screen readers | `AutomationProperties.Name` on all icon-only buttons and nav items; **custom `AutomationPeer`s** for HealthGauge ("PC health score: 84 of 100"), TreemapControl (list of children with name+size), charts (summary string, e.g. "CPU 34%, trending flat"). |
| Keyboard | `Focusable="True"` + `CyberFocusVisual` on HealthGauge, treemap nodes, chips-as-links, tool cards; verify tab order per page = visual order. |
| High contrast | Listen to `SystemParameters.HighContrast`: swap glass surfaces → solid (`CyberPanelSolidColor`), disable CyberBackground + all glows, 1px solid borders. Also exposed as a manual toggle ("Solid surfaces") for high-ambient-light users. |
| Reduce transparency | Settings toggle collapsing Surface1–3 alphas to opaque equivalents (map exists in Cyberpunk.xaml's solid fills). |
| Motion | Already excellent (reduce-motion contract) — no changes. |
| Text scaling | Because sizes move into styles (§4.2), a future "Large text" toggle becomes a single-dictionary swap; not shippable today but the remap is the prerequisite. |

---

## 8. Animation & Interaction Recommendations

The v4 motion system (duration ramp, behavior catalog, Freezable rules, reduce-motion contract)
is the best part of the codebase. **Recommendation: add nothing to the framework; add three
choreographed moments and two rules.**

1. **Score delta ticker** — after Fix all, the gauge sweeps to the new value (existing) *plus* a
   small `+6` chip that rises 8px and fades (400ms, CubicEase Out). Reward without fanfare.
2. **Success sweep** — one-shot 600ms diagonal `GlassSheenBrush` sweep across the hero card when
   a long operation completes. One per operation, never looping, reduce-motion → skip.
3. **Palette match highlight** — matched substrings tint cyan as-you-type (no animation needed;
   perceived responsiveness comes from the instant filter).
4. **Rule: motion budget per screen** — one NeonPulse (existing law), max one RevealOnLoad flash,
   stagger caps at 400ms total (existing) — hubs with 12 cards must not take 1.5s to settle.
5. **Rule: no new hover choreography** — HoverLift/HoverGlow cover 100% of cases; variety here
   reads as inconsistency, not delight.

---

## 9. Mobile & Responsive Design Strategy

Honest scoping: SystemCare is a WPF desktop app (Win 10/11, MinWidth 960) — there is no mobile
target, and inventing one would be scope theater. "Responsive" translates to **window-resize
resilience** across 960 → ultrawide:

- `AdaptiveColumns` behavior (§4.4) replaces every hardcoded `UniformGrid Columns`.
- `ContentMaxWidth 1100` token on all scroll pages; hubs may use 1280.
- Data grids: `Auto`/star columns + `SharedSizeGroup` + `MinWidth`; no fixed-pixel content columns.
- Sticky summary footers (`Surface3`) stay viable at 960 by collapsing labels to icons+numbers.
- Test matrix (add to release checklist): 960×600, 1240×780 (default), 1920×1080, 3440 ultrawide,
  at 100/125/150% DPI.
- The existing 180→200px pane remains fixed-width; at <1100px the pane auto-collapses to
  icons-only (`PaneDisplayMode=LeftCompact`) — WPF-UI supports this without forking.

---

## 10. Implementation Roadmap

Sequenced for value-per-risk; each phase ships independently.

| Phase | Scope | Effort | Exit criteria |
|---|---|---|---|
| **1. Foundation & enforcement** | §4.1–4.2 token/type changes; SmokeTest lint rules (initially warn-only); retrofit the 6 worst drifted pages + Dashboard | ~1 wk | CI lint green on retrofitted pages; contrast audit passes |
| **2. Full retrofit** | Remaining ~40 pages to tokens; lint flips to fail; radius/spacing snap | ~2 wks, parallelizable | Zero inline FontSize/Opacity in Views |
| **3. Navigation** | Title-bar search affordance; palette recents/pins/highlighting; icon reassignments; category hub pages; pane restructure; breadcrumbs | ~2 wks | Nav pane ≤ 10 items; hubs live; palette discoverable |
| **4. Accessibility** | AutomationPeers, focus visuals, names; high-contrast + reduce-transparency modes | ~1.5 wks | Narrator walkthrough of Dashboard + one scan page; keyboard-only Fix all |
| **5. Dashboard & templates** | Score-input chips + Fix-all preview; CyberStatCard/CyberToolCard; scan-page template rollout; AdaptiveColumns | ~2 wks | Score explainable; 7 scan pages on one template |
| **6. Polish** | Delta ticker, success sweep, Settings regroup, resize test matrix | ~1 wk | Release checklist updated |

Governance: v5 tokens append to `DESIGN-SYSTEM.md` (additive, no renames); SmokeTest keys updated
per existing rule.

---

## 11. Expected User Experience Impact

- **Legibility/trust:** every text element ≥ AA; captions grow 11→12px, body 13→14px — the app
  stops whispering its own content. Explainable score converts skeptics.
- **Time-to-tool:** pins + recents + visible search collapse the 90% case from scan-43-rows to
  one click / one keystroke; hubs give first-time users a browsable map with status-based scent.
- **Perceived quality:** one type ramp, one chip system, one card recipe across 49 pages is the
  difference between "app" and "suite of plugins" — consistency *is* the premium signal.
- **Inclusivity:** the score, charts, and treemap become perceivable to screen-reader users for
  the first time; keyboard users can run the core loop end-to-end; high-contrast users get a
  usable mode without losing the brand.
- **Durability:** CI lint means the redesign can't erode — v5 is the last time a "fix the drift"
  project is needed.
