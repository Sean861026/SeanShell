# SeanShell design system

SeanShell uses a native Fluent visual language with a macOS-inspired floating
glass Dock for a dense developer workspace.
The application follows the active Windows theme and accent color instead of
shipping a separate brand palette.

## Principles

- Prefer WinUI controls, system brushes, and Segoe UI Variable.
- Keep one primary action per surface and preserve keyboard-first operation.
- Use a 4/8 px spacing rhythm and consistent surface, type, and icon roles.
- Use color together with text or an icon; never use color as the only status.
- Keep effects subtle so idle CPU, memory use, and gaming compatibility remain
  first-class requirements.
- Borrow interaction qualities rather than Apple identity: floating glass,
  restrained depth, rounded icon grouping, and concise status surfaces. Do not
  copy Apple logos, fonts, icons, or fixed brand colors.

## Tokens and styles

Shared resources live in
`src/SeanShell.App/Styles/SeanShellStyles.xaml`.

The initial roles are:

- spacing: 4, 8, 12, 16, 20, 24, and 32 px;
- surfaces: regular card, metric card, compact result, and status pill;
- typography: page title, section title, metric label, metric value, secondary
  text, and caption;
- interaction: native WinUI button, list, toggle, focus, and high-contrast
  behavior.

New UI should consume these roles before adding one-off dimensions or colors.
Raw colors must not be embedded in individual views.

## Accessibility and motion

- Interactive targets should be at least 44 px high where space allows.
- Keyboard focus must remain visible and follow visual reading order.
- Normal text should meet WCAG AA contrast in light and dark themes.
- Text must remain usable with Windows text scaling and high-contrast themes.
- High contrast disables translucent backdrops and motion while retaining native
  ThemeResource colors; text scaling expands both Dock height and its fixed
  launcher/system area before applying monitor-width bounds.
- Dock hover feedback uses a 220 ms, bottom-anchored 1.35 scale with a 6 px
  lift; press feedback uses a 90 ms 0.92 scale. The fixed layout slot prevents
  reflow while the hovered item rises above its siblings. Both resolve to an
  identity transform with no transition when Windows requests reduced effects
  or Gaming Mode is active.
- Auto-hide expands with a 180 ms ease-out fade and 12 px upward translation;
  collapse uses a faster 120 ms ease-in exit before resizing to the edge
  indicator. The transition animates only opacity and translation, remains
  interruptible, and resolves immediately when reduced effects are active.
- Launcher and new-application activation uses one 420 ms two-step decaying
  bounce on an independent render-transform track. It confirms the click without
  reflowing neighbors and is omitted entirely for reduced effects. Ordinary
  running-window switch and minimize actions deliberately remain still.
- Dock ListView containers keep native pointer-over, pressed, and selection
  surfaces transparent. The icon, badge, running indicator, and keyboard focus
  visual carry interaction state without exposing the container's tall safety
  lane during magnification.
- Dock icons use 192 px BGRA snapshots sourced from the executable's 256 px
  Shell jumbo image before falling back to the window-provided icon. The 104 px
  normal Dock height and centered 88 px item lane retain stable hit targets.
  A click-through native layered window bilinear-scales cached icons to 76 px
  and supplies true per-pixel alpha above the glass base. Running icons carry
  their active accent halo and active/running/minimized rail into that floating
  surface, so state never appears detached below the magnified icon.
  Missing/fallback icons, reduced effects, and any native update failure remain
  in the safe Dock lane.
- The transient Dock uses desktop Acrylic; the dashboard and Launcher use Mica.
  Every backdrop retains semantic Layer/Card fallback brushes, and raw tint
  colors are not embedded in the Dock.
- The Dock glass shell uses a 24 px outer radius without a drawn outer stroke or
  full-width highlight. Its translucent material defines the silhouette, while
  16 px application/system capsules provide the internal structure.
  Theme-specific neutral layers keep the desktop Acrylic visibly frosted even
  on dark wallpapers. Reduced effects replaces these layers with the native
  opaque card surface while preserving the hierarchy.
- The Dock removes native caption/frame and extended edge styles after the HWND
  has loaded and after every AppWindow placement update, because presenter
  resize/move operations can restore `WS_DLGFRAME`. The presenter can preserve
  that native style even after a direct update, so the visible HWND region is
  clipped two device-independent pixels inside the frame with a DPI-aware
  rounded region. DWM non-client rendering is explicitly disabled so Windows
  does not redraw an outline along that region; client-area Acrylic remains
  active. The Dock never hooks or injects into another process.
- Dock chrome has three semantic depth levels: Acrylic window shell, a tertiary
  application region, and a secondary bordered system region. Clock figures use
  tabular numerals so time changes do not shift neighboring content.
- The SeanShell brand mark is the only fixed-color identity asset. Its rounded
  blue terminal prompt is self-contained so it remains legible on light, dark,
  transparent, Start, taskbar, title-bar, and installer surfaces. Package assets
  are reproducibly generated by `tools/generate-brand-assets.ps1`.
- Dock state never depends on color alone. Active windows combine an accent
  surface with the wide underline, running and minimized windows retain distinct
  underline lengths and emphasis, and pinned applications carry a compact pin
  badge. Tooltips and automation names repeat the same state in text.
- Application artwork and the Launcher brand mark share the 40 px icon token and
  magnify only their visual layer, leaving the 44 px interaction target stable.
  System controls and the clock use contained 1.03 hover feedback without lift,
  so their wider content remains aligned and cannot escape the Dock shell.
- Application hover also gives the nearest visible icon on either side a
  subordinate 1.14 scale and 2 px lift. The three visual transforms never resize
  their layout lanes, and Windows reduced-effects mode disables the complete
  magnification wave.

## Planned surfaces

1. Adaptive dashboard information architecture.
2. Launcher result grouping, stable feedback, and keyboard hints.
3. Dock running, active, minimized, hover, and focus states.
4. System theme, high contrast, compact density, and visual regression checks.
