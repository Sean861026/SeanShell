# Dock keyboard switching validation

## Policy tests

`DockKeyboardNavigationTests` covers previous/next wrap, first/last, empty/invalid
selection, large-count arithmetic, stable group identity across refresh/reorder,
closed-group fallback, and foreground selection when keyboard focus is elsewhere.
Existing window-cycle tests cover selecting the next foreground-relative group.
Policy tests do not instantiate WinUI or guarantee focus access to an exclusive
full-screen or elevated application.

## Interactive checks

1. In Normal mode, Ctrl+Alt+D still starts at Launcher. Tab to running applications.
   Left/Right wrap, Home/End reach boundaries, and Tab exits to the other controls.
2. Enter/Space on a single-window group restores/activates it without minimizing
   the previously active window. A mouse click continues to use normal toggle.
3. A multi-window group opens a native picker; keyboard selection of a window and
   Escape dismissal preserve native menu behavior.
4. Temporarily enable manual Gaming mode, bring a safe test window to foreground,
   and press the configured Dock shortcut. After its one-shot refresh, focus goes
   directly to the next running group on the current display. The normal inventory
   timer remains paused; no graphics/input hooks or synthetic game input are added.
5. While navigating, change another window's title or close it. Selection stays on
   the same group if it survives; if removed, it uses the nearest remaining group.
   A background Dock must not steal focus from a different application.
6. Switch away or press Escape during keyboard entry: a late refresh must not
   reclaim focus. Exit SeanShell while a refresh is pending: no closed UI updates.
7. Restore the original Gaming mode and close only test-created windows.

Exclusive-full-screen games may hide the Dock or reject normal focus changes.
Borderless/windowed mode and Windows' native Alt+Tab remain compatibility fallbacks;
this feature is not a secure-desktop/UIPI bypass or an anti-cheat overlay.

Implementation uses documented [WinUI keyboard events](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-events)
and [scoped focus management](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui).

## Local Release validation, 2026-09-30

Using the existing temporary input-layout probe as a harmless window target:

- Normal mode retained Launcher entry; Tab and Enter returned to the active probe
  without minimizing it.
- Manual Gaming mode entered the next running group after the explicit refresh.
  End, Right wrap, Space activation, and Escape return were verified visually.
- Two probe instances formed a group; Enter opened its native picker, Down moved
  to the other window, and Enter activated it.
- Manual mode was restored to off, automatic detection stayed enabled, and the
  probe reported zero input-layout changes.

An initial synchronous focus check missed the first entry; a single low-priority,
request-guarded dispatch fixed the observed timing issue and was retested. Refresh
identity/fallback cases are covered by Core tests; exclusive full-screen games,
anti-cheat compatibility, and every asynchronous focus race were not tested here.
