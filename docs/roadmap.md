# Roadmap

## M0: Foundation

- .NET 10 and WinUI 3 solution
- Core state, Windows boundary, gaming policy, and plugin contracts
- Architecture, flow, safety, and contribution documents
- Automated build and unit tests
- [x] Opt-in Windows sign-in startup with live OS state and crash-loop protection
- [x] Single-instance activation routing with duplicate-Dock prevention

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
- [x] Defer the hidden Launcher XAML window until its first user request
- [x] Coalesce reused-Launcher reset into one provider refresh
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
- Lazy-Launcher Release sample on the primary development machine: before the
  first Launcher request, 254.9 MB average working set and 194.0 MB private
  memory over ten half-second samples, down from 277.4 MB and 208.2 MB in the
  immediately preceding eager-window build. After first use, the reusable
  Launcher settles at 273.4 MB working set and 202.8 MB private memory.

## M3: Gaming compatibility

- [x] Versioned local settings with atomic writes and backup recovery
- [x] Persistent Dock auto-hide preference
- [x] Configurable Launcher shortcut presets with conflict rollback
- [x] Configurable active-display Dock focus shortcut with independent registration and conflict rollback
- [x] Source-aware manual and rule-based gaming mode
- [x] Persisted opt-in process rules with schema v1 to v2 migration
- [x] Pause/resume policy for dashboard sampling and all Dock windows
- [x] Add bounded automatic-detector CPU and P95 scan diagnostics
- [x] Persist the 20 most recent detected-session compatibility summaries
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
- [x] Bounded external manifest, path, hash, and Authenticode candidate diagnostics
- [x] Atomic per-publisher and per-capability consent with user revocation
- [x] Versioned, bounded, fail-closed out-of-process broker health handshake
- [x] Fail-closed publisher revocation diagnostics and explicit trust recheck
- [x] Short-lived capability-bound broker metadata probe without code execution
- [x] One-process Job Object limits and fail-closed broker process mitigations
- [x] Suspended broker creation with restricted handle inheritance before resume
- [x] Per-process HMAC-authenticated, single-use broker request/response channel
- [x] Persistent per-plugin broker crash accounting and automatic quarantine
- [x] Package-contained broker mode using the exact SeanShell App executable
- [x] MSIX package-content integrity enforcement and packaged broker CI coverage
- [x] Bounded same-publisher managed/native dependency allowlist and broker revalidation
- [x] Inactive allowlist-only managed/native resolver with no host fallback
- [x] Strict bounded data-only command DTOs and canonical command-set digest
- [ ] Capability-restricted broker activation and out-of-process isolation
- [ ] Production certificate pipeline and signed external loading through an isolated broker
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
- [x] DPI-aware Launcher geometry with bounded mixed-display placement
- [x] Dock- and foreground-aware Launcher targeting across mixed-DPI displays
- [x] Dock active, minimized, hover, and keyboard-focus states
- [x] Active-application visual continuity while the Dock owns keyboard foreground
- [x] Desktop Acrylic Dock surface with reduced-effects-aware hover and press motion
- [x] Unified glass application capsule, floating icon tiles, and branded accent rail
- [x] Semantic application and system-area surface hierarchy with stable tabular clock figures
- [x] Windows-provided icons for running and pinned applications with Fluent fallback
- [x] Post-ranking Windows icons for visible Launcher application results
- [x] Unplated Launcher brand icons with accent-tile Fluent fallbacks
- [x] Flushed WinUI icon buffers before compositor invalidation
- [x] Per-icon failure isolation with accessible Fluent fallback
- [x] Native icon extraction failure isolation from desktop-window snapshots
- [x] Asynchronous SoftwareBitmapSource loading with no blank icon state
- [x] ExtraLarge 192px Shell icon snapshots with bounded high-DPI downsampling
- [x] Production SeanShell brand icon across package, title-bar, and Dock Launcher surfaces
- [x] Icon-first taskbar presentation with distinct active, running, and minimized indicators
- [x] Shape, surface, badge, tooltip, and automation state parity for Dock items
- [x] Persisted system, light, and dark appearance preference with schema v4 migration
- [x] Light, dark, keyboard-focus, and two-display visual checks
- [x] High-contrast and text-scaling layout checks through native Windows signals
- [x] Follow Windows reduced-motion, high-contrast, and text-scale signals
- [x] Persisted comfortable and compact display density with schema v5 migration
- [x] Reduced-effects Gaming Mode removes translucent Dock material and pauses background providers
- [x] Shared acrylic-card visual language for Dock system flyouts
- [x] Compact network, audio, power, and battery status cluster on every Dock
- [x] Semantic offline, mute, charging, and low-battery glyph/color states
- [x] Layout-stable macOS-inspired primary and nearest-neighbor icon magnification
- [x] Per-pixel-alpha native icon magnifier that extends beyond the Dock shell
- [x] Floating active, running, and minimized state parity during magnification
- [x] Rich Fluent taskbar tooltips with state and interaction guidance
- [x] Overlay accent halo that remains visible above opaque active-app icons

## Later: shell modes

- [x] Opt-in Companion Taskbar with Explorer retained for shell services
- [x] Hide and restore primary and secondary Windows taskbars
- [x] Independent owner-process recovery guard with readiness handshake
- [x] Persisted taskbar preference introduced in schema v6 and preserved by later migrations
- [x] Multi-display, restart, graceful-exit, and forced-exit recovery checks
- [x] Pinned applications and Launcher/Start affordance in every Dock
- [x] Guaranteed running-window viewport beside fixed Dock controls
- [x] Non-activating Dock startup with a keyboard-accessible Dashboard recovery action
- [x] Persisted bounded pin list with schema v7 migration and missing-shortcut recovery
- [x] Coalesce desktop shortcut pins with matching monitor-local running windows
- [x] Pin and unpin directly from running and standalone Dock item context menus
- [x] Persisted Move left / Move right pin ordering synchronized across displays
- [x] Native drag-to-reorder for visible pins with hidden running-pin preservation
- [x] Regional-format clock and date on every Dock
- [x] Native keyboard-accessible monthly calendar in the Dock clock flyout
- [x] On-demand network and battery/power status in Dock Quick settings
- [x] Native master-volume Slider and mute control in Dock Quick settings
- [x] Unconstrained clock and Quick settings flyouts above the compact Dock root
- [x] User-initiated native system-area reveal without input simulation or tray enumeration
- [x] Automatic replacement resume when Gaming Mode starts
- [x] Reserve/recalculate each monitor work area with bounded Dock AppBars
- [x] Per-monitor immersive mode releases AppBar space for maximized/full-screen windows
- [x] Preserve immersive work areas across cross-monitor foreground changes
- [x] Event-driven immersive detection with debounced transitions and fallback reconciliation
- [x] Event-driven Dock inventory updates with cache invalidation and fallback reconciliation
- [x] Session-stable per-monitor running-app order with append-only new groups
- [x] Native drag-to-reorder for monitor-local running-app groups
- [x] Keyboard-accessible running-app Move left / Move right context actions
- [x] Public-API virtual-desktop filtering with fail-open compatibility behavior
- [x] Immediate virtual-desktop switch reconciliation through documented WinEvent
- [x] Scale Dock geometry correctly across mixed-DPI monitors
- [x] Accessible previous/next and mouse-wheel navigation for bounded Dock overflow
- [x] Standard taskbar click toggle: minimize active, restore and activate others
- [x] Window context actions for minimize, restore, and graceful close
- [x] Process-based multi-window grouping with an accessible native window picker
- [x] Per-window context actions inside multi-window groups
- [x] Safe Close all windows action for multi-window groups
- [x] Group-wide minimize and non-activating restore actions
- [x] Live DWM hover previews with per-window switch and graceful-close actions
- [x] Theme-aware preview cards with application identity and explicit window state
- [x] Layout-aware DWM previews that fill each available aspect-fit surface
- [x] Bounded post-layout recovery for transient DWM preview registration failures
- [x] Layout-stable loading and final-failure states for live window previews
- [x] Monitor-DPI-correct preview geometry when Dock XAML reports identity scale
- [x] Full-card preview surfaces with non-activating, layout-safe window startup
- [x] Safe Open new instance action for explicitly matched running applications
- [x] Standard middle-click new-instance behavior with ambiguity-safe selection
- [x] Native Show desktop / Restore windows toggle without simulated input
- [x] Keyboard-only Dock entry point with active-display targeting and auto-hide expansion
- [x] Reduced-motion-aware auto-hide entry and exit transitions
- [x] Reduced-motion-aware Launcher and pinned-application activation feedback
- [x] New-instance feedback on running applications without animating window switches
- [x] Fixed-size accessible Dock status for empty and unavailable window inventories
- [x] Escape dismissal returns to the previous foreground window and collapses the Dock
- [x] Explorer recovery script and automatic-start crash-loop protection
- [x] Native Dock background menu for settings, Task Manager, system-area access, and safe exit
- [x] Verified local executable actions for file location and elevated launch
- [x] Verified executable actions on unpinned running-application context menus
- [x] Standard Shift-click new-instance behavior for pinned and running applications
- [x] Standard Ctrl+Shift-click elevated launch for verified local executables
- [x] Standard Ctrl-click cycling across running multi-window groups
- [x] Read-only Full shell edition preflight with fail-closed Dashboard status
- Full shell experiment only on supported Windows editions
- Full shell remains opt-in and is not a 1.0 requirement

Replacement functionality takes priority over additional visual polish. New
effects, animation refinements, and production iconography resume after the
Companion Taskbar supplies the essential pinned-app and system affordances.
