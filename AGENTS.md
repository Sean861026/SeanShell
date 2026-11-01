# Local visual validation

- The owner requires UI reviews on the notebook's **built-in display**, not the
  external monitor. Confirm the active display topology before choosing a Dock
  or test window; do not assume that the primary display is the notebook panel.
- On this workstation the built-in panel identifies as `SDC41A3` (internal output)
  and the external monitor as `AUS3437`. Display numbers and coordinates can change,
  so identify the panel again each session instead of hard-coding `DISPLAY1`.
- Keep screenshots, interactions, and Activity Island visual acceptance on the
  built-in panel unless the owner explicitly requests external-display testing.
- Automated geometry tests may still cover multi-monitor layouts. They do not
  replace live visual/focus acceptance, and capture failures must not be reported
  as a passing UI test.
