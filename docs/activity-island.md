# Activity Island preview

Activity Island is an optional, transient status capsule inspired by the idea of
a compact activity surface. It complements SeanShell's Dock; it is not another
taskbar, a notification-center replacement, or a permanent screen cutout.

## Enable and try it

Right-click an empty area of the Dock and select **Activity Island (preview, this
session)**. The toggle applies to every SeanShell Dock in the current process.
It starts off, is not saved to settings, and returns to off after SeanShell exits.
Select the same menu item again to disable it and dismiss any visible capsule.

Enabling displays a short confirmation on the invoking Dock's display, subject
to the suppression rules below. Then use the Dock to activate an already-running
application. When that exact window becomes foreground, the capsule shows its
process name and window title on the active application's display.

Supported paths include direct single-window activation, keyboard activation,
the multi-window picker, context-menu activation, and Ctrl-click window cycling.
Minimizing an application does not produce an activation notification. Preview-card
clicks, newly launched processes, Alt+Tab, and foreground changes initiated outside
the Dock are not activity sources in this prototype.

## Presentation and lifecycle

- A single lazily created, borderless window appears at the top center of the
  target monitor's work area. The nominal size is 360 by 76 DIPs with a 16-DIP
  top margin; physical placement uses that monitor's DPI.
- Windows text-size scaling increases its height without enlarging its width;
  long labels remain single-line and ellipsized.
- Small work areas shrink the window and margins so the native rectangle remains
  inside the work area. This bounds geometry but does not guarantee readable
  content on unrealistically tiny work areas.
- The window is always-on-top while visible but is shown without activation and
  omitted from window switchers. It does not register an AppBar, reserve desktop
  space, or change the foreground application's work area.
- Each event replaces the previous content and restarts a 2.4-second dismissal
  deadline. There is no notification queue or persistent history.
- A pointer press dismisses the capsule early. It currently has no expand,
  playback, or command controls and is not a keyboard-focus destination.
- Windows reduced-effects preferences remove the optional entrance animation
  and Acrylic backdrop. The opaque fallback uses theme resources.
- Display changes dismiss the current capsule; subsequent events use a fresh
  monitor snapshot. Shell shutdown closes the window and stops its timer.

The prototype displays only information already available in SeanShell's window
inventory. It does not inspect application content or add global keyboard hooks.
This feature does not send activity data to a service or persist a title history.

## Gaming and full-screen behavior

Gaming Mode suppresses the capsule and dismisses it when the mode begins.
Full-monitor window coverage on the target display also suppresses it. This
coverage check is deliberately conservative; it is not a promise to classify
every application-specific full-screen mode. Ordinary maximization alone is not
a suppression condition unless the resulting window covers the monitor bounds.

The existing immersive reconciliation path dismisses a visible capsule when
coverage changes. The island itself has no polling loop. It never changes
SeanShell's taskbar replacement or recovery configuration.

## Verification checklist

Core tests cover 100%, 150%, and 200% scaling, negative monitor coordinates, tiny
work areas, invalid inputs, and all enable/gaming/coverage/shutdown combinations.
Those tests validate geometry and policy, not live WinUI presentation.

On 2026-11-01 the local Release build and automated tests passed. A live launch
was attempted, but the capture returned overlapping text and no accessibility
tree, so the preview toggle and visual/focus checks could not be validated.
The test instance was stopped; the checklist below remains open. Do not treat
the capture symptom as a confirmed application rendering diagnosis.

A same-day retry explicitly targeted the notebook's internal `SDC41A3` panel
(2560x1600, negative desktop X coordinates), not the external `AUS3437` display.
Rebinding the notebook Dock and refreshing its capture reproduced the mixed text;
accessibility data was still unavailable. The process loaded the installed
Windows App Runtime **2.6.4.0**, despite the project's SDK reference being 2.3.1.
Upstream reports describe similar cross-GPU/mixed-DPI text corruption:
[WinUI #12434](https://github.com/microsoft/microsoft-ui-xaml/issues/12434) and
[WinUI #12464](https://github.com/microsoft/microsoft-ui-xaml/issues/12464).
These are diagnostic leads, not proof of this application's root cause.
Read-only adapter mapping identified Intel Arc for the notebook panel and NVIDIA
RTX 4060 Laptop for the external panel. SeanShell has no explicit per-application
GPU preference; automatic selection does not establish which GPU rendered a frame.
The packaged and build-output resource indexes matched, and their managed module
identities matched, making stale deployment less likely.
No GPU preference, display arrangement, driver, or runtime was changed during
the retry. Follow `AGENTS.md`: this workstation's visual acceptance stays on the
notebook panel, even when the external display is configured as primary.

The following are manual acceptance checks, not claims of completed live testing:

- [ ] Start SeanShell: no island appears until the session toggle is enabled.
- [ ] Enable from each display; confirm the capsule appears on the invoking display.
- [ ] Activate an application through each supported Dock path; continue typing
  in that application and confirm the capsule never takes keyboard focus.
- [ ] Minimize a foreground window; confirm it does not show an activation capsule.
- [ ] Trigger two activations quickly; confirm only the latest content remains
  and its dismissal deadline is not shortened by the earlier event.
- [ ] Click the capsule to dismiss; otherwise confirm dismissal after about 2.4 seconds.
- [ ] Test mixed-DPI displays, negative monitor origins, and long window titles;
  confirm on-screen placement and readable clipping/trimming.
- [ ] Enter Gaming Mode or full-monitor coverage while visible; confirm dismissal
  and no island over a game or full-screen application.
- [ ] Test reduced motion and high contrast; confirm the animation/material fallback
  and legible text, without a change to application focus.
- [ ] Change display topology, disable the toggle, and exit SeanShell while visible;
  confirm no orphan window. Restart and confirm the preference is off again.
- [ ] Confirm taskbar recovery, maximized work areas, and Dock auto-hide are unchanged.

## Next increments

Future sources should reuse bounded, existing services rather than introduce a
second monitoring subsystem:

- Media sessions: now-playing identity and explicit playback controls.
- Audio: short volume/mute feedback, with duplicate-event coalescing.
- Input: confirmed keyboard-layout changes, without claiming that a layout
  switch also changes an IME's Chinese/English composition mode.
- Plugins: opt-in, bounded progress events with expiry and cancellation state.
- Interaction: accessible expanded controls, deliberate keyboard entry/return,
  event priority, and predictable dismissal.
- Preferences: persisted enablement, placement and source controls only after
  the transient prototype's focus, accessibility, and gaming checks pass.

These are planned extensions; none is part of the current activation-only preview.
