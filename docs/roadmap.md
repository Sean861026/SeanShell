# Roadmap

## M0: Foundation

- .NET 10 and WinUI 3 solution
- Core state, Windows boundary, gaming policy, and plugin contracts
- Architecture, flow, safety, and contribution documents
- Automated build and unit tests

## M1: Launcher

- [x] Global hotkey using `RegisterHotKey` without input hooks
- [x] Installed application and system setting providers
- [x] Ranked, cached, keyboard-first results
- [ ] Measure cold-window and cached-query performance on representative hardware
- Target: cached results under 50 ms; first window under 300 ms

## M2: Dock and dashboard

- [x] Current-window enumeration and user-initiated activation
- [x] Lightweight primary-display dock with gaming-mode suspension
- [x] Live CPU and memory cards with two-second sampling
- [x] One monitor-local dock per startup display snapshot
- [x] Edge-peek auto-hide with pointer and keyboard-focus safeguards
- [ ] Rebuild dock windows after display hot-plug without restarting
- [ ] Recent repositories, Git status, Docker and WSL providers
- Target: idle CPU below 0.5% and working set below 200 MB
- Current local Release sample: 0.31% average CPU and 155 MB working set over
  15 seconds with the dashboard and dock visible; longer hardware coverage remains.
- Current multi-display Release sample after auto-hide: 0.14% average CPU,
  201 MB working set, and 151 MB private memory over 15 seconds. The additional
  WinUI composition surface slightly exceeds the original working-set target and
  remains an optimization item.

## M3: Gaming compatibility

- [x] Versioned local settings with atomic writes and backup recovery
- [x] Persistent Dock auto-hide preference
- [x] Configurable Launcher shortcut presets with conflict rollback
- Manual and rule-based gaming mode
- Pause/resume policies with measured resource use
- Compatibility matrix for Steam and anti-cheat-enabled games
- No injection, graphics hooks, overlays, or input interception

## M4: Plugin platform

- Versioned manifest and capability model
- Timeouts, fault isolation, signing policy, and plugin diagnostics
- Built-in Git, Docker, WSL, and OpenTAP plugins

## Later: shell modes

- Companion shell experiment with Explorer retained for shell services
- Recovery drills and crash-loop protection
- Full shell experiment only on supported Windows editions
- Full shell remains opt-in and is not a 1.0 requirement
