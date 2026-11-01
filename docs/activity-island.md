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

The preview also confirms explicit keyboard-layout changes made through the Dock:
focus an application, open the Dock, then choose **Quick settings > Input methods**
and select a different loaded layout. After Windows confirms the target's layout,
the capsule shows **KEYBOARD LAYOUT** and that layout's display name. The exact
target must still exist and remain foreground with the requested layout active.
Already-active layouts, posted-but-unconfirmed requests, failures, and results
arriving after focus moved do not produce feedback.

This describes a keyboard layout, **not** the IME's Chinese/English composition
mode. External/system keyboard changes are not watched. There is no new polling
loop, keyboard hook, installed language, or system-default input preference.

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
- Changed content replaces the previous content and restarts a 2.4-second
  dismissal deadline. Identical content on the same monitor while visible is
  coalesced without extending the deadline. There is no notification queue or
  persistent history; an expired or dismissed message can be shown again.
- Updates on the same monitor do not replay the entrance animation or recreate
  Acrylic. A new appearance or monitor transition uses the existing entrance;
  reduced-effects preference changes still apply through the shared accessibility
  path. A vanished target monitor drops the event rather than moving it to primary.
- A pointer press dismisses the capsule early. It currently has no expand,
  playback, or command controls and is not a keyboard-focus destination.
- Windows reduced-effects preferences remove the optional entrance animation
  and Acrylic backdrop. The opaque fallback uses theme resources.
- Display changes dismiss the current capsule; subsequent events use a fresh
  monitor snapshot. Shell shutdown closes the window and stops its timer.

The prototype displays information already available in SeanShell's window and
loaded-keyboard-layout inventories. It does not inspect application content or
add global keyboard hooks. Visible headings, icons, and full accessibility names
distinguish app activation, keyboard-layout confirmation and enablement preview.
An accessibility name alone does not imply an automatic screen-reader announcement.
This feature does not send activity data to a service or persist a title history.

## Gaming and full-screen behavior

Gaming Mode suppresses the capsule and dismisses it when the mode begins.
Full-monitor coverage by the top eligible application on the target display
also suppresses it. Each monitor uses its own top-to-bottom window order; a
normal application above a background full-monitor window permits the capsule.
SeanShell, shell, minimized, invisible, and other non-taskbar windows are excluded.
This coverage check is not a promise to classify every application-specific
full-screen mode. Ordinary maximization alone is not a suppression condition
unless the top application's resulting window covers the monitor bounds.

The existing immersive reconciliation path dismisses a visible capsule when
coverage changes. The island itself has no polling loop. It never changes
SeanShell's taskbar replacement or recovery configuration.

## Activation-preview verification — 2026-11-01 (PR #175)

Follow `AGENTS.md`: visual acceptance on this workstation is restricted to the
notebook's built-in `SDC41A3` panel, even when external `AUS3437` is primary.
No external-display screenshot or interaction was used for this acceptance.

### Automated and code review

- Release x64 solution build: zero warnings and errors; **609 tests passed**,
  zero skipped, including broker tests against the freshly built app executable.
- Bundled-runtime/package-integrity checks, formatting verification, and
  `git diff --check` passed.
- Geometry tests cover 100%, 150%, and 200% scaling, negative monitor coordinates,
  text scaling, tiny work areas and invalid inputs. Policy tests cover every
  enable/gaming/coverage/shutdown combination and per-monitor Z-order suppression.
- Cleanup tests cover failing cleanup and diagnostics while preserving
  out-of-memory propagation. Independent review checked timer/shutdown failure
  isolation and architecture-compatible, read-back-verified native window styles.

### Notebook live acceptance

- Session default-off and reset-to-off after a normal restart were observed.
- The notebook Dock's multi-window picker activated two owned QA windows with
  long Traditional Chinese/English titles. The Island appeared at the notebook's
  top center at 150% scaling, with negative desktop X coordinates.
- A bounded, read-only native trace recorded the target QA window retaining
  foreground while the Island was visible. `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW`
  were present. One complete show/hide interval measured **2,404 ms** (40-ms sampling).
- Re-activating the owned QA app in actual full-monitor mode via the Dock did
  not display an Island. The fixture constrained all fullscreen transitions to
  `SDC41A3`; it did not change Windows display configuration.
- The owner confirmed the capsule's appearance on the built-in panel was normal
  and explicitly approved merging. This is **owner-confirmed visual acceptance**:
  the desktop tool did not enumerate the non-activating Island as a capture target,
  so no direct automated Island screenshot pass is claimed.
- Both QA windows and SeanShell were closed normally. Native inspection found no
  orphan Island; work areas and the original settings remained unchanged.
  Windows taskbar replacement stayed off throughout this run.

The live run caught and fixed background fullscreen coverage incorrectly hiding
the Island behind a normal foreground application. Island suppression now follows
the top eligible application on each monitor; existing Dock work-area policy is
unchanged.

### Extended checks still to perform

These are not claims of completed live testing. Retain them before promoting the
session-only preview to persisted, expanded controls:

- Exercise every activation path, minimization, rapid replacement of notifications,
  and pointer-press early dismissal under varied application responsiveness.
- Exercise reduced motion, high contrast and enlarged text using real OS settings.
- Exercise display hot-plug/topology changes and taskbar-replacement recovery with
  a visible Island. Multi-monitor geometry remains covered automatically; do not
  use the external panel for visual acceptance without explicit owner direction.

### Earlier rendering investigation

Earlier retries in this PR were blocked by corrupted captures using the installed
Windows App Runtime 2.6.4. An explicitly authorized temporary per-app power-saving
GPU preference did not resolve that symptom; both test values were removed and
automatic selection was restored. No global GPU or display settings were changed.
The current run incorporates the stable bundled-runtime fix from PR #176. Those
earlier failed captures are diagnostic history, not passing visual tests.

## Keyboard-layout increment verification — 2026-11-01

- Release x64 build passed with zero warnings/errors; **655 tests passed**, zero
  skipped, using the freshly built app for broker integration tests. Runtime
  integrity, formatting and diff checks passed. Independent reviews covered the
  confirmation gate, lifecycle subscriptions, typed content and timer integration.
- The first CI run exposed a five-second startup-observation timeout in the
  existing process-cancellation fixture, before it requested cancellation. Its
  test-only setup now observes both startup and operation completion within the
  existing 15-second query budget, preserving early failures. Real-process tests
  run outside parallel execution; four virtual-time regressions cover this setup.
  Production deadlines, three-second deadline tests and the post-cancellation
  five-second assertion are unchanged. The exact CI timing cause was not
  reproduced locally; this is not a claim of a production cancellation defect.
- Deterministic tests cover stale/reused targets, changed focus/layout, every
  non-confirmed result, full semantic labels, duplicate coalescing, replacement
  near expiry, old queued timer ticks, dismissal and overflow-safe timing.
- The isolated `SeanShell.InputLayoutProbe --notebook-only` opened only on the
  built-in `SDC41A3` panel. The actual Dock flow changed its target-thread layout
  from `04040404` to `04090409`, with the probe observing one change and Dock
  reporting confirmation. The Island's native window was visible on that panel
  without becoming foreground; a 40-ms sampling trace measured a 2,416-ms lifetime.
- The probe retained input focus and accepted test text. Selecting the already
  active English layout left its change counter at one, reported already-active
  status and did not show another Island during the sampled interval.
- The same Dock flow restored `04040404`; refreshed probe status showed original
  and current layout equal and two observed changes. No user application was an
  input-change target, and no installed/default language, audio, GPU or display
  setting was modified. The probe and SeanShell then exited normally.
- Screenshots/interactions stayed on the notebook. As with PR #175, the capture
  tool cannot enumerate the non-activating capsule, so this record claims native
  lifecycle/focus and Dock/probe UI verification, **not** a new automated capsule
  visual or screen-reader pass. Capsule geometry and typography remain unchanged;
  kind-specific text/icon mapping is covered by the content tests.

## Next increments

Future sources should reuse bounded, existing services rather than introduce a
second monitoring subsystem:

- Media sessions: now-playing identity and explicit playback controls.
- Audio: short volume/mute feedback, with duplicate-event coalescing.
- Plugins: opt-in, bounded progress events with expiry and cancellation state.
- Interaction: accessible expanded controls, deliberate keyboard entry/return,
  event priority, and predictable dismissal.
- Preferences: persisted enablement, placement and source controls only after
  the transient prototype's focus, accessibility, and gaming checks pass.

These are planned extensions; none is part of the current activation and
confirmed-keyboard-layout preview.
