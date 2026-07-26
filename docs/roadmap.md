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
- [x] In-app first-usable and bounded cached-query P95 diagnostics
- [x] Validate cold-window and cached-query targets on representative hardware
- Target: cached results under 50 ms; first window under 300 ms
- Current local Release sample: 42 ms first usable and 8 ms cached-query P95
  across five repeated empty-query Launcher openings on the primary development
  machine.

## M2: Dock and dashboard

- [x] Current-window enumeration and user-initiated activation
- [x] Lightweight primary-display dock with gaming-mode suspension
- [x] Live CPU and memory cards with two-second sampling
- [x] One monitor-local dock per startup display snapshot
- [x] Edge-peek auto-hide with pointer and keyboard-focus safeguards
- [x] Rebuild dock windows after display hot-plug without restarting
- [x] Cached recent repositories and Git status Launcher provider
- [x] Cached WSL distribution Launcher provider
- [x] Cached Docker container provider with offline-Engine handling
- Target: idle CPU below 0.5% and working set below 200 MB
- Current local Release sample: 0.31% average CPU and 155 MB working set over
  15 seconds with the dashboard and dock visible; longer hardware coverage remains.
- Current multi-display Release sample after auto-hide: 0.14% average CPU,
  201 MB working set, and 151 MB private memory over 15 seconds. The additional
  WinUI composition surface slightly exceeds the original working-set target and
  remains an optimization item.
- Shared-snapshot Release sample with two auto-hidden Docks and all current
  built-in plugins: 0.261% average CPU, 212.2 MB average working set, and
  166.5 MB average private memory over 15 seconds. Per-monitor enumeration tasks
  are removed, but the WinUI surface working-set target remains open.

## M3: Gaming compatibility

- [x] Versioned local settings with atomic writes and backup recovery
- [x] Persistent Dock auto-hide preference
- [x] Configurable Launcher shortcut presets with conflict rollback
- [x] Source-aware manual and rule-based gaming mode
- [x] Persisted opt-in process rules with schema v1 to v2 migration
- [x] Pause/resume policy for dashboard sampling and all Dock windows
- [x] Add bounded automatic-detector CPU and P95 scan diagnostics
- [ ] Record detector diagnostics during representative game sessions
- Current local Release idle sample: 0.042% estimated detector CPU and 14.3 ms
  scan P95 over 28 samples with configured rules and no active match.
- Compatibility matrix for Steam and anti-cheat-enabled games
- No injection, graphics hooks, overlays, or input interception

## M4: Plugin platform

- [x] Versioned schema-1 manifest and capability model
- [x] Bounded lifecycle/query calls with session fault isolation
- [x] Fluent dashboard diagnostics for explicitly registered built-in plugins
- [x] Gaming Mode suspend/resume integration
- [x] Built-in Developer tools Launcher plugin
- [ ] Signed external loading, consent, revocation, and process isolation
- [x] Persistent per-plugin enable/disable controls with schema v3 migration
- [x] Built-in read-only Git repository plugin
- [x] Built-in WSL distribution plugin
- [x] Built-in Docker plugin
- [x] Built-in .NET workspace plugin for C#, ASP.NET Core, and Blazor
  - [x] Discover `.sln`, `.slnx`, and `.csproj` from configured roots without a
    whole-disk scan
  - [x] Cache project type, target frameworks, and solution files
  - [x] Add Launcher commands for the default IDE, VS Code, and terminal
  - [x] Keep build, test, and run actions explicit and show the exact `dotnet`
    command before execution
  - [x] Add Blazor/ASP.NET Core hot reload and validated local launch-profile URLs

## UI polish

- [x] Shared Fluent spacing, shape, typography, surface, and list styles
- [x] Adaptive dashboard information architecture
- [x] Dashboard visual hierarchy with a branded Hero, icon-led cards, and persistent status summaries
- [x] Launcher search hierarchy, result surfaces, and stable feedback states
- [x] Dock active, minimized, hover, and keyboard-focus states
- [x] Persisted system, light, and dark appearance preference with schema v4 migration
- [x] Light, dark, keyboard-focus, and two-display visual checks
- [ ] High-contrast and text-scaling visual checks on representative Windows settings
- [x] Follow Windows reduced-motion and text-scale signals; high contrast remains native ThemeResource behavior
- [x] Persisted comfortable and compact display density with schema v5 migration
- [x] Reduced-effects Gaming Mode removes Mica and pauses background providers

## Later: shell modes

- Companion shell experiment with Explorer retained for shell services
- [x] Explorer recovery script and automatic-start crash-loop protection
- Full shell experiment only on supported Windows editions
- Full shell remains opt-in and is not a 1.0 requirement
