# SeanShell design system

SeanShell uses a native Fluent visual language for a dense developer workspace.
The application follows the active Windows theme and accent color instead of
shipping a separate brand palette.

## Principles

- Prefer WinUI controls, system brushes, and Segoe UI Variable.
- Keep one primary action per surface and preserve keyboard-first operation.
- Use a 4/8 px spacing rhythm and consistent surface, type, and icon roles.
- Use color together with text or an icon; never use color as the only status.
- Keep effects subtle so idle CPU, memory use, and gaming compatibility remain
  first-class requirements.

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
- Dock hover feedback uses a 120 ms scale/translation transition; press feedback
  uses 80 ms. Both resolve to an identity transform with no transition when
  Windows requests reduced effects or Gaming Mode is active.
- Backdrop surfaces use BaseAlt Mica with semantic Layer/Card fallback brushes;
  raw tint colors are not embedded in the Dock.

## Planned surfaces

1. Adaptive dashboard information architecture.
2. Launcher result grouping, stable feedback, and keyboard hints.
3. Dock running, active, minimized, hover, and focus states.
4. System theme, high contrast, compact density, and visual regression checks.
