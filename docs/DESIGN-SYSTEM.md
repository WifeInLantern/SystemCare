# SystemCare Design System (v4)

The cyberpunk "Night City" visual language: neon cyan + magenta on near-black, layered glass
surfaces, disciplined glow, and a motion system that is **always Reduce-motion aware**. v4 is
additive-only over v3 — stronger glow/contrast and richer easing on the same identity, no new
dictionaries, no renamed keys. This doc is the contract for anyone adding UI — tokens and styles
live in three dictionaries merged in `App.xaml` in a load-bearing order:

```
ui:ThemesDictionary (Dark)  →  ui:ControlsDictionary  →  Styles/Theme.xaml
  →  Styles/Cyberpunk.xaml  →  Styles/Components.xaml
```

`tools/SmokeTest` verifies every token/style key below in CI — add new keys there when you add them
here. **Never rename or remove an existing key.**

## 1. Palette (Theme.xaml)

| Token | Hex | Use |
|---|---|---|
| `AccentColor` / `NeonCyanBrush` | `#00E5FF` | Primary neon — accents, focus, hero glow |
| `SecondaryColor` / `NeonMagentaBrush` | `#FF2A6D` | Secondary neon, gradient partner, danger |
| `VioletColor` / `NeonVioletBrush` | `#B14CFF` | Tertiary accent (charts, gradients) |
| `SuccessColor` / `SuccessBrush` | `#00FFA3` | Positive stats, healthy states |
| `WarningColor` / `WarningBrush` | `#FFD300` | Needs-attention states |
| `DangerColor` / `DangerBrush` | `#FF2A6D` | Errors, at-risk states |
| `DangerSoftBrush` | `#FF8AA8` | Readable error captions on subtle danger fills |
| `TextPrimaryColor` | `#E6F6FF` | Primary text |
| `TextSecondaryColor` | `#8FA6C0` | Secondary text |
| `TextTertiaryColor` | `#6E86A6` | Captions, hints |

C# call sites (custom-drawn controls, animation glows, the accent manager) must read colors via
**`Helpers/CyberPalette`**, never hardcode hexes — that's what makes a Theme.xaml edit propagate
everywhere.

## 2. Surfaces & glass (elevation scale)

Alpha increases with elevation so panels read as stacked glass over the animated backdrop:

| Token | Value | Elevation |
|---|---|---|
| `Surface0Brush` | `#FF0A0E14` | Page floor |
| `Surface1Brush` | `#A611192A` | Resting cards (also the `ui:Card` fill) |
| `Surface2Brush` | `#C81A2438` | Rows, inputs, hover surfaces, skeletons |
| `Surface3Brush` | `#F0202C46` | Flyouts, dialogs, sticky headers (v4: alpha raised from `#E6` for a clearer "lifted" read) |
| `GlassRimBrush` | white→stroke vertical gradient | The 1px top rim-light stroke that sells the glass |
| `GlassSheenBrush` | `#14FFFFFF → transparent` | Optional top sheen overlay |

v4 also deepened `CyberBackgroundDeepColor` from `#05070B` to `#03040A` — a small widen of the
background gradient range, more contrast for panels to sit on. `CyberBackgroundColor`,
`CyberPanelColor`, `CyberStrokeColor`, `AccentColor`, `SecondaryColor` are identity anchors and stay
untouched.

Hand-rolled panels use `CyberGlassPanel` (Surface1 + rim) or `CyberGlassPanelRaised` (Surface2).

## 3. Status tints (translucent, ~20% alpha)

`SuccessSubtleBrush #3300FFA3`, `WarningSubtleBrush #33FFD300`, `DangerSubtleBrush #33FF2A6D`,
`InfoSubtleBrush #3300E5FF`, `VioletSubtleBrush #33B14CFF`, `AccentSubtleBrush #2200E5FF`, plus
matching `*SubtleStroke` at `0x55` alpha. Use via the chip styles: `CyberChipSuccess/Warning/
Danger/Info` with `ChipTextSuccess/Warning/Danger/Info` labels. Never inline a `#33…` literal.

## 4. Glow discipline

| Effect | Blur / opacity | Law |
|---|---|---|
| `GlowSm` / `GlowSuccessSm` | 12 / 0.40 | Static accents: section headers, icons |
| `GlowMd` / `GlowMagentaMd` / `GlowVioletMd` | 22 / 0.52, 20 / 0.58, 20 / 0.55 | Hover states (prefer the `HoverGlow` behavior) |
| `GlowLg` | 34 / 0.68 | **Max 1–2 hero elements per page** (gauge, primary CTA) |
| `GlowXl` | 44 / 0.75 | **Reserved — one per app, not one per page.** The single loudest hero (e.g. the health gauge). Distinct from `GlowLg`'s per-page allowance so pages don't all reach for the loudest tier. |

(v4 pushed every value one step stronger than v3; `GlowVioletMd` and `GlowXl` are new.) Never put a
glow on body text. Max one *breathing* (NeonPulse) element per screen.

## 5. Typography ramp

| Style | Font / size | Use |
|---|---|---|
| `CyberDisplayText` | Orbitron Bold + glow | Hero numbers/wordmarks only |
| `CyberPageTitle` | Rajdhani SemiBold 26 + soft glow | Page H1 |
| `CyberSectionHeader` | Orbitron SemiBold 15, cyan + glow | Section headers |
| `TextH2` | 15 SemiBold | Card/group titles |
| `TextH3` | 13 SemiBold | Sub-sections (replaces ad-hoc `FontSize=12 SemiBold`) |
| `TextBody` | 13, secondary, wraps | Body copy |
| `TextCaption` | 11, tertiary | Descriptions, hints (replaces `FontSize="12" Opacity="0.65"`) |

Numerals use Rajdhani (`LabelFontFamily`); display type uses Orbitron (`DisplayFontFamily`).

## 6. Spacing & radii

4px base scale: `SpaceXs 4 · SpaceSm 8 · SpaceMd 12 · SpaceLg 16 · SpaceXl 24 · Space2Xl 32`,
`PagePadding 24`, `PadCard 16,12`, `PadChip 7,3`, `PageMargin 24,12,24,24`. Radii: `CardRadius 4`
(angular cyber corners), `RadiusMd 8`, `RadiusPill 999`.

## 7. Motion system

All motion is **code-driven attached behaviors** in `Helpers/Animations.cs`. Durations come from
the `Motion` constants:

| Token | ms | Easing | Use |
|---|---|---|---|
| `Motion.Fast` | 120 | CubicEase Out | Press-down, chip hover, focus glow in |
| `Motion.Base` | 200 | CubicEase Out | Hover-in glow/lift, press release (BackEase 0.3) |
| `Motion.Gentle` | 300 | CubicEase Out | Hover-out, fade-swaps, badge changes |
| `Motion.EntranceMs` | 280 | CubicEase Out | `FadeInOnLoad` (v4: was 260) |
| `Motion.RevealMs` | 340 | CubicEase Out | `RevealOnLoad` (+ one-shot power-on flash) (v4: was 320) |
| `Motion.StaggerStepMs` | 40 | — | Sibling delay, capped at 400ms total |
| `Motion.LoopMs` | 1600 | SineEase InOut | NeonPulse breathing |
| `Motion.ShimmerLoopMs` | 1100 | SineEase InOut | Skeleton shimmer |

`Motion.Entrance`/`Reveal`/`Loop` `TimeSpan` properties now exist alongside the `*Ms` doubles (v4 —
previously these three were inline `TimeSpan.FromMilliseconds` literals; use the named properties).

**Laws:** nothing interactive exceeds 300ms; hover is in=Base / out=Gentle; only entrances go
longer (via stagger delay); one breathing element per screen.

### Behavior catalog

| Attached property | What it does |
|---|---|
| `Animations.StaggerChildren` (Panel) | Auto-staggered entrance for children; skips collapsed / `StaggerExclude="True"` / already-annotated children. **The default way to animate a page** — put it on the page's root panel. |
| `Animations.FadeInOnLoad` + `RevealDelay` | Single-element entrance (fade + 12px rise) |
| `Animations.RevealOnLoad` | Entrance + one-shot cyan power-on flash (hero elements) |
| `Animations.FadeVisible` (bool binding) | Animated Visibility swap for busy/results states |
| `Animations.HoverGlow` (+`HoverGlowColor`) | Glow that fades in/out on hover (default accent) |
| `Animations.HoverLift` | Scale 1.02 + glow that **fades in/out like HoverGlow** (v4: was an instant pop before). **Never combine with HoverGlow/NeonPulse** — each owns `Effect`. |
| `Animations.PressScale` | Press-down 0.96 / spring-back (implicit on all `ui:Button`) |
| `Animations.NeonPulse` | Forever-breathing glow — heroes only, one per screen |
| `Animations.Shimmer` | Opacity breathing for `SkeletonBlock`/`SkeletonCard` |
| `Animations.SmoothValue` | Glides a ProgressBar/RangeBase value |
| `Animations.CountUpText` (+Format/Bytes/Suffix) | Counting numeric TextBlock |
| `Animations.EntranceSpring` (v4) | Opt-in subtle `BackEase` overshoot (amplitude 0.25) on `FadeInOnLoad`/`RevealOnLoad`'s Y-translate only — never opacity, never `Effect`. For a couple of true hero elements (health gauge, a primary CTA), not bulk `StaggerChildren` cascades. |

### WPF Freezable rules (why behaviors, not Style storyboards)

1. A Freezable supplied by a Style **Setter** (RenderTransform, Effect, Brush) is frozen and shared
   when the style seals — animating it throws `Cannot resolve all property references…`.
2. `BeginStoryboard` in Style.Triggers is safe only for plain DPs on the element itself (Opacity).
3. Anything honoring Reduce motion must be code-driven — XAML storyboards can't consult
   `Animations.ReduceMotion`. Hence: **new motion = a new attached behavior** using the
   per-instance `GetTransforms()` pattern.

### Reduce-motion contract

Every loop (NeonPulse, Shimmer, TaskProgress pulse, CyberBackground) subscribes
`Animations.ReduceMotionChanged` and freezes/restarts in place on a live toggle. Every entrance
settles instantly (`Opacity = 1`). Every value animation snaps. The nav transition switches to
`None` (MainWindow). Test both directions of the Settings toggle when adding motion.

## 8. Component recipes

- **Card**: `ui:Card` (implicit style = Surface1 fill, animated hover glow + cyan border).
  Interactive/clickable → `CyberInteractiveCard` (HoverLift). Padding via `CyberCard`.
- **Glass panel** (non-Card layout surface): `CyberGlassPanel` / `CyberGlassPanelRaised` on a Border.
- **Buttons**: `CyberPrimaryButton` / `CyberGhostButton` / `CyberDangerButton`; all get PressScale
  from the implicit `ui:Button` style and an animated HoverGlow.
- **Status chip**: `<Border Style="{StaticResource CyberChipSuccess}"><TextBlock Style="{StaticResource ChipTextSuccess}" Text="OK"/></Border>`
- **Loading state**: skeletons behind the swap, real example from `CleanupPage.xaml` (scan results) —
  ```xaml
  <Grid>
      <StackPanel helpers:Animations.FadeVisible="{Binding IsBusy}">
          <Border Style="{StaticResource SkeletonCard}" />
          <Border Style="{StaticResource SkeletonCard}" />
          <Border Style="{StaticResource SkeletonCard}" />
      </StackPanel>
      <ScrollViewer helpers:Animations.FadeVisible="{Binding IsBusy, Converter={StaticResource Not}}">
          <ItemsControl ItemsSource="{Binding Categories}" … />
      </ScrollViewer>
  </Grid>
  ```
  Fixed placeholder borders (not a per-item template) avoid unnecessary `Shimmer`-host churn on
  rapid scan/cancel/rescan cycles.
- **Empty state** (composition, no control): centered StackPanel of `ui:SymbolIcon` (FontSize 40,
  Opacity 0.4) → `EmptyStateTitle` → `EmptyStateHint` → optional `CyberGhostButton`.

## 8b. Navigation

`MainWindow.xaml`'s `NavigationView` keeps `Transition="FadeInWithSlide"` (WPF-UI's `Transition`
enum has no easing hook, so a deeper transition redesign would mean forking `NavigationView`
internals — out of scope). `TransitionDuration` is `260` (v4: was 220), aligned just under
`Motion.RevealMs` since a full-page transition is the largest "reveal" in the app and shouldn't
feel faster than a card's own reveal. `MainWindow.xaml.cs`'s `ApplyNavigationTransition()` swaps to
`Transition.None` under Reduce motion live (duration is irrelevant when `None`).

## 9. New-page checklist

1. Root panel: `helpers:Animations.StaggerChildren="True"` (backgrounds/overlays get
   `StaggerExclude="True"`).
2. Title = `CyberPageTitle`; sections = `CyberSectionHeader`; captions = `TextCaption` (never
   `FontSize="12" Opacity="0.65"`); sub-headers = `TextH3`.
3. Chart accents = `{StaticResource AccentColor}` etc. — never hex literals.
4. Status badges = `CyberChip*` styles / `*SubtleBrush` tokens.
5. Long operations: `TaskProgress` + `FadeVisible` skeleton swap.
6. Add any new tokens/styles to `tools/SmokeTest/Program.cs`.
7. Verify with the Reduce motion toggle both ways.

## 10. Design System v6 — "Night City, Refined" (2.13.0)

Additive pass implementing the foundation phase of `docs/UI-REDESIGN-V5.md`. No keys renamed or
removed; identity anchors untouched.

**New tokens** (Theme.xaml + SmokeTest): `TextQuaternaryBrush #586C88` (~3.2:1 — legitimate 4th
de-emphasis tier for ≥18px ghost text; exists so pages stop reaching for `Opacity="0.5"`),
`Space3Xl 48`, `ContentMaxWidth 1100` (max width for scrolling page content).

**New style**: `TextMetricHero` — Rajdhani SemiBold 32, primary; the big live numbers (CPU %, RAM),
replacing inline `FontSize="32" FontWeight="Bold"`.

**Ramp widened** (the old 15/13/13/11 spread was so compressed pages kept inventing inline sizes):
`TextH2` 15→17, `TextH3` 13→14, `TextBody` 13→14 (+ LineHeight 20), `TextBodyStrong` 13→14,
`TextMetricValue` 13→14, `TextCaption` 11→12 (+ LineHeight 17).

**Value change**: `TextFillColorDisabledBrush` `#3A4A60` (2.1:1) → `#5A6E8C` (~3.6:1).

**New laws**:
- **Text-opacity ban.** `Opacity` is never applied to `TextBlock`/`Run` in Views. Hierarchy comes
  from the four text brushes and the ramp. (Computed: `TextSecondary × 0.7` on Surface0 = 4.28:1 —
  below AA; stacking opacity on tokens silently breaks contrast.)
- **Inline-size ban.** `FontSize` in Views is a defect; sizes live in the ramp styles only.
- **One glyph = one tool** in the nav (2.13.0 reassigned: Browser Cleanup `GlobeClock24`,
  Network `Router24`, Secure DNS `LockShield24`, Debloat `BoxDismiss24`,
  Breach Checker `Password24`, Security Audit `ShieldGlobe24`).

**Navigation**: a "Search" entry at the top of the pane opens the Ctrl+K command palette (the
palette itself is unchanged); tooltip advertises the hotkey.
