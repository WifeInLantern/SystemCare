# SystemCare Design System (v3)

The cyberpunk "Night City" visual language: neon cyan + magenta on near-black, layered glass
surfaces, disciplined glow, and a motion system that is **always Reduce-motion aware**. This doc is
the contract for anyone adding UI — tokens and styles live in three dictionaries merged in
`App.xaml` in a load-bearing order:

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
| `Surface3Brush` | `#E6202C46` | Flyouts, dialogs, sticky headers |
| `GlassRimBrush` | white→stroke vertical gradient | The 1px top rim-light stroke that sells the glass |
| `GlassSheenBrush` | `#14FFFFFF → transparent` | Optional top sheen overlay |

Hand-rolled panels use `CyberGlassPanel` (Surface1 + rim) or `CyberGlassPanelRaised` (Surface2).

## 3. Status tints (translucent, ~20% alpha)

`SuccessSubtleBrush #3300FFA3`, `WarningSubtleBrush #33FFD300`, `DangerSubtleBrush #33FF2A6D`,
`InfoSubtleBrush #3300E5FF`, `VioletSubtleBrush #33B14CFF`, `AccentSubtleBrush #2200E5FF`, plus
matching `*SubtleStroke` at `0x55` alpha. Use via the chip styles: `CyberChipSuccess/Warning/
Danger/Info` with `ChipTextSuccess/Warning/Danger/Info` labels. Never inline a `#33…` literal.

## 4. Glow discipline

| Effect | Blur / opacity | Law |
|---|---|---|
| `GlowSm` / `GlowSuccessSm` | 10 / 0.35 | Static accents: section headers, icons |
| `GlowMd` / `GlowMagentaMd` | 18 / 0.45–0.5 | Hover states (prefer the `HoverGlow` behavior) |
| `GlowLg` | 28 / 0.6 | **Max 1–2 hero elements per page** (gauge, primary CTA) |

Never put a glow on body text. Max one *breathing* (NeonPulse) element per screen.

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
| `Motion.EntranceMs` | 260 | CubicEase Out | `FadeInOnLoad` |
| `Motion.RevealMs` | 320 | CubicEase Out | `RevealOnLoad` (+ one-shot power-on flash) |
| `Motion.StaggerStepMs` | 40 | — | Sibling delay, capped at 400ms total |
| `Motion.LoopMs` | 1600 | SineEase InOut | NeonPulse breathing |
| `Motion.ShimmerLoopMs` | 1100 | SineEase InOut | Skeleton shimmer |

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
| `Animations.HoverLift` | Scale 1.02 + glow (interactive cards). **Never combine with HoverGlow/NeonPulse** — each owns `Effect`. |
| `Animations.PressScale` | Press-down 0.96 / spring-back (implicit on all `ui:Button`) |
| `Animations.NeonPulse` | Forever-breathing glow — heroes only, one per screen |
| `Animations.Shimmer` | Opacity breathing for `SkeletonBlock`/`SkeletonCard` |
| `Animations.SmoothValue` | Glides a ProgressBar/RangeBase value |
| `Animations.CountUpText` (+Format/Bytes/Suffix) | Counting numeric TextBlock |

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
- **Loading state**: skeletons behind the swap —
  ```xaml
  <StackPanel helpers:Animations.FadeVisible="{Binding IsBusy}">
      <Border Style="{StaticResource SkeletonCard}" />
      <Border Style="{StaticResource SkeletonBlock}" />
  </StackPanel>
  <ItemsControl helpers:Animations.FadeVisible="{Binding HasResults}" … />
  ```
- **Empty state** (composition, no control): centered StackPanel of `ui:SymbolIcon` (FontSize 40,
  Opacity 0.4) → `EmptyStateTitle` → `EmptyStateHint` → optional `CyberGhostButton`.

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
