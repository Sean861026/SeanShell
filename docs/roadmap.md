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
- [ ] Multi-monitor placement and auto-hide
- [ ] Recent repositories, Git status, Docker and WSL providers
- Target: idle CPU below 0.5% and working set below 200 MB
- Current local Release sample: 0.31% average CPU and 155 MB working set over
  15 seconds with the dashboard and dock visible; longer hardware coverage remains.

## M3: Gaming compatibility

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
