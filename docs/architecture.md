# Architecture

## Purpose

SeanShell is a modular developer workspace for Windows. The first releases use
supported Windows APIs and retain Explorer as the shell-service owner. The
replacement priority is a recoverable Companion Taskbar before any full shell
deployment mode.

## Operating modes

1. **Overlay mode:** Explorer owns the desktop, taskbar, tray, and shell services.
   SeanShell supplies its own launcher, dock, and dashboard windows.
2. **Companion Taskbar mode:** Explorer remains available for shell services
   while every Windows taskbar is hidden and SeanShell's monitor-local docks
   become the visible window-switching surface. An independent recovery guard
   restores the taskbars if the main process exits.
3. **Full shell mode:** SeanShell becomes the configured shell only on supported
   Windows editions and only after recovery and compatibility gates pass.

`FullShellReadinessService` is the first boundary for Full shell mode. It reads
`ProductName` and `EditionID` from the Windows current-version registry key and
delegates policy to the platform-neutral `FullShellReadinessResolver`. The
resolver recognizes the Enterprise, Enterprise LTSC, Education, and IoT
Enterprise edition families documented for Microsoft Shell Launcher. Unknown or
unsupported editions fail closed. This stage is deliberately diagnostic only:
it exposes no mutation API and does not query or change Shell Launcher WMI,
optional Windows features, Winlogon, the current user SID, or Explorer startup.

## Components

- `SeanShell.App` is the WinUI 3 composition root. It contains views and binds
  platform services to feature modules.
- `SeanShell.Core` owns immutable models, shell state, and command abstractions.
- `SeanShell.Windows` isolates Win32, process, registry, shell, and Authenticode
  trust integration.
- `SeanShell.Gaming` owns process rules and the policy for pausing optional work.
- `SeanShell.PluginContracts` is the small, versioned surface available to plugins.
- `SeanShell.Plugins` validates manifests and owns bounded plugin lifecycle,
  launcher queries, fault isolation, and diagnostics.
- `SeanShell.PluginBroker.Protocol` defines bounded process messages without
  referencing plugin contracts or the App.
- `SeanShell.PluginBroker.Runtime` owns the identity-free broker entry point,
  process mitigations, key-pipe reader, and protocol session.
- Packaged production composition starts the exact `SeanShell.App.exe` in
  `--plugin-broker` mode. The custom entry point selects this path before WinUI
  initialization. `SeanShell.PluginBroker` is a console test harness over the
  same runtime. Protocol v4 accepts a health handshake and a short-lived,
  capability-bound metadata probe, but contains no loading or activation path.
- projects under `plugins/` contain explicitly registered built-in implementations.

Dependencies point inward: App may depend on every module; Gaming and
PluginContracts depend on Core; the plugin host depends on Core and contracts;
the Windows boundary depends on Core and the plugin host's narrow trust-verifier
contract. Core depends only on .NET. Plugins never receive the App service
container or direct access to UI internals.

The built-in Git plugin receives a small list of repository roots from the App
composition root. The App first walks upward from its current and binary
directories to resolve a containing repository, which keeps packaged build output
discoverable without scanning arbitrary working directories. Common developer
folders use breadth-first discovery capped at depth two and twelve repositories,
skip reparse points and build/dependency folders, and never scan all of the user's
Documents directory. Repository metadata is read by a
cancellable `git status` child process during initialization or an explicit
Launcher refresh. Launcher queries use the cached immutable snapshots.

Pinned applications reuse `InstalledApplicationProvider`'s one-time Start Menu
index. Settings contain at most eight ordered `app:` command IDs; the provider
resolves only exact IDs that exist in that index and returns the original
`ShellCommand` launch delegate. Missing shortcuts are omitted from the current
Dock snapshot but remain in settings so a restored shortcut can reappear. The App
distributes one immutable pinned-command list to every monitor-local Dock. It also
shares the already cached application index so Dock context menus can offer
bounded pin candidates without rescanning the filesystem. `TaskbarDockPinResolver`
requires an explicit shortcut process identity, preserves ambiguous matches as
separate choices, and never infers identity from a window title.
The same resolved application candidates power Open new instance from running
window context menus. SeanShell executes the original cached `ShellCommand`; it
does not construct an executable path or force applications that enforce a
single-instance policy to create another process.
Launcher application results use the same `InstalledApplicationProvider` icon
cache only after the search service returns its bounded ranked list. At most the
eight visible results request native extraction. Each `LauncherResultViewModel`
starts with a Fluent fallback, then swaps to the shared `SoftwareBitmapSource` on
the UI thread without delaying query ranking or first usable presentation.
`LauncherWindowPlacement` converts the command palette's effective-pixel design
size through `DisplayScaleLayout` before calling physical-pixel `AppWindow`
placement APIs. It centers horizontally, uses an upper-third vertical bias, and
constrains both axes to the monitor work area with a scale-aware 24-DIP margin.
`LauncherTargetMonitorResolver` gives an invoking Dock's monitor precedence,
then uses the foreground window's monitor for global shortcut and Dashboard
requests, before bounded primary/first-display fallbacks. Placement reads the
selected monitor's work area and DPI rather than the Launcher's previous HWND DPI.
`PinnedApplicationOrder` performs only an adjacent, case-insensitive ID swap.
Boundary commands are disabled before dispatch, and a successful move atomically
persists the ordered ID list before refreshing every Dock.

The Dock is icon-first rather than a row of text cards. Each taskbar item keeps a
fixed hit target and icon slot while `TaskbarItemVisualStateResolver` maps the
foreground/minimized snapshot to one of three stable presentations: a wide
accent active underline, a compact running marker, or a compact minimized marker
with reduced icon emphasis. Tooltips and automation names preserve the window
title, process, and state that are intentionally omitted from the visual surface.
During hover magnification, the click-through layered icon window renders the
same state as a floating accent halo and rail; the Dock hides its in-lane state
visuals only after the native per-pixel-alpha update succeeds.
`DockForegroundContinuity` retains the captured previous window as active only
while no live application snapshot owns foreground and that exact HWND still
exists. This prevents the active rail from disappearing when keyboard navigation
makes the Dock itself foreground, without guessing a replacement window.
`TaskbarDockLayout` derives a bounded expanded width from the current pinned and
running item counts, capped to both the preferred maximum and the monitor work
area. This keeps sparse Docks compact while preserving horizontal scrolling for
dense sessions and small displays. The normal window budget includes breathing
room beyond each item's visual width, so typical sessions expand before a
horizontal scrollbar is needed. Its fixed-control allowance includes the
Launcher and complete system region so those controls cannot consume the
monitor-local running-window viewport. Fixed icon buttons explicitly override
the native Button minimum size while retaining a 44-by-44 interaction target.
When the Dock reaches its monitor-bound maximum, the running-window viewport
replaces its thin horizontal scrollbar with explicit previous/next Fluent
buttons. `DockOverflowNavigation` keeps their enabled state and page target
bounded; mouse-wheel paging uses the same path, and reduced-effects mode disables
the optional scroll animation.
Dock dimensions remain device-independent inside Core and are converted to
physical pixels at the `AppWindow` boundary using the target monitor's effective
DPI. A one-shot post-show refresh handles the WinUI/`WM_DPICHANGED` startup race
without adding continuous polling.

The Dock's transparent Layer surface allows its desktop Acrylic system backdrop
to remain visible instead of covering it with an opaque card. Acrylic is used
only for the transient, always-on-top taskbar surface; the larger dashboard and
Launcher retain Mica. All foreground, stroke, and fallback colors remain Windows
ThemeResources. Pointer input resolves through `DockItemMotion` to a bounded
1.03 scale and one-pixel lift on hover or a 0.97 press state. MainWindow
propagates the existing reduced-effects policy to every Dock; Windows reduced
motion, high contrast, and Gaming Mode remove Acrylic, transitions, scale, and
translation while retaining native focus and pointer states.

Application icons with a cached `ApplicationIconSnapshot` use a separate native
`LayeredDockIconWindow` during hover. It bilinear-downsamples the premultiplied
192px BGRA buffer into a `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`
popup and updates it through `UpdateLayeredWindow`, allowing true per-pixel alpha
above the Acrylic HWND. The original icon is hidden only after the native update
succeeds and is restored on exit, collapse, display rebuild, Gaming Mode, or
shutdown. Fluent fallback glyphs retain the bounded in-Dock animation.

Normal Dock creation and display-topology rebuilds use non-activating
`AppWindow.Show(false)` calls, so an auto-hiding Dock cannot replace the
Dashboard or the user's current application as foreground merely by appearing.
The explicit Dock keyboard shortcut remains the only path that activates a Dock.
The Launcher/Start context menu also exposes a native Open Dashboard command,
which restores and activates the existing MainWindow without creating another
process or window.

The expanded Dock groups floating application icons inside one
Layer-on-Acrylic capsule rather than giving every icon an opaque tile. Only the
foreground application receives an accent backing. A bounded system-accent
gradient rail provides brand identity on expanded and collapsed surfaces without
changing the interaction geometry. These additions use Windows theme colors;
reduced-effects mode still replaces Acrylic with the existing opaque card surface.

`TaskbarWindowGrouper` combines monitor-local windows by case-insensitive process
name and keeps the foreground window first inside each group. Generic Windows
host processes remain separate because their process name is not a safe
application identity. A single-window
group preserves direct taskbar toggle behavior. A multi-window group displays a
numeric badge and opens a native `MenuFlyout` containing every title and state.
The native control owns keyboard focus, arrow navigation, Enter activation, and
Escape dismissal; SeanShell does not add a global input hook. Grouping reduces
visual density while retaining individual window handles for activation. Its
context menu mirrors that same window list as native submenus whose labels retain
the current Active, Running, or Minimized state, each containing Activate,
Minimize or Restore, and graceful Close window commands. Single-window menus
expose the same explicit Activate action. Every Dock flyout uses one compact
native presenter style with a bounded width, shared corner radius, and Fluent
icons while retaining system theme, high-contrast, and keyboard behavior. Duplicate
titles receive a stable ordinal in both menus so every handle remains
distinguishable without process injection.

Hovering a running Dock group starts a bounded delay before opening one reusable
`WindowPreviewWindow`. `WindowPreviewLayout` caps the surface at six windows and
uses a one-, two-, or three-column grid. Each card keeps its title and graceful
close action outside the preview rectangle. `DwmThumbnail` owns the native
source-to-destination relationship and unregisters it whenever the preview hides,
the Dock snapshot changes, or SeanShell shuts down. DWM supplies the live pixels;
SeanShell does not capture, poll, inject, or hook the source process. Selecting a
preview uses the same validated restore-and-activate boundary as the Dock, while
the existing native window picker and context menu remain the keyboard
alternatives to hover-only discovery.

Desktop Start Menu shortcuts receive an optional process identity by resolving
their `.lnk` target through the Windows Shell Link COM contract during the
one-time application index. Each monitor-local Dock uses
`TaskbarPinWindowMatcher` to suppress a standalone pin only when that explicit
identity matches a live window process. It never guesses from titles. Unsupported
URL, ClickOnce, packaged, or unresolved shortcuts therefore fail open as normal
pins instead of coalescing unrelated applications. A coalesced running item
retains Unpin from Dock, while a standalone pin exposes the same command from its
own context menu. Both surfaces also expose Move left and Move right against the
same global pin order, including when another display coalesces a pin with its
running window.

The built-in .NET workspace plugin receives the same bounded developer roots. A
breadth-first scan is capped at depth four and 24 solution/project files, skips
build, dependency, reparse-point, and inaccessible directories, and never scans
the entire disk. Project inspection uses an XML reader with DTD processing and
external resolution disabled. Only the project SDK, target frameworks, selected
references, and the presence of nearby Razor component files are used to classify
the in-memory snapshot. Launcher queries never touch the filesystem.
`launchSettings.json` is optional and parsed with comments/trailing commas
enabled to match common .NET templates. Only absolute HTTP/HTTPS loopback URLs
are cached, capped at eight per project; external hosts and non-web schemes are
discarded.

The M2 dock receives immutable `DesktopWindowSnapshot` records from a Windows-only
service. The UI never calls `EnumWindows` or activation APIs directly. System CPU
and memory sampling follows the same boundary and publishes a
`SystemMetricsSnapshot` to the dashboard. MainWindow owns one two-second Dock
refresh loop, captures one immutable window snapshot, and distributes it to every
monitor-local Dock. Docks filter that shared snapshot by monitor and never start
their own enumeration task.

Dock window clicks resolve against live foreground and minimized state rather
than the cached snapshot. An active visible window receives `SW_MINIMIZE`;
another or minimized window receives `SW_RESTORE` when needed followed by
`SetForegroundWindow`. This reproduces normal taskbar toggle semantics without
injecting code or attaching input queues.

The Dock context menus expose only bounded window operations. A single-window
item presents them directly; a multi-window group nests the same operations
under one submenu per window. Minimize and restore reuse the same service
boundary; Close posts `WM_CLOSE` to the window instead of terminating its
process, preserving the application's opportunity to cancel or prompt for
unsaved work. While a menu is open, Dock auto-hide is paused and resumes after
the flyout closes. Open new instance remains an application-level action outside
the per-window submenu because it launches the matched shortcut rather than
operating on a specific window handle.
Middle-click follows the same cached-identity boundary: standalone pins execute
their exact command, a running item with one application candidate opens it, and
multiple candidates require a native selection flyout. A running window without
a reliable application identity is left unchanged.

Every Dock also exposes a native Show desktop toggle. `ShowDesktopSession`
tracks only SeanShell-initiated minimize-all state and delegates to the Windows
Shell `MinimizeAll` and `UndoMinimizeAll` automation methods. It does not
synthesize Win+D. A foreground application detected by the shared window
snapshot invalidates the pending restore state, and Shell automation failure
leaves the previous toggle state unchanged.

`NativeApplicationIconReader` stays in the Windows boundary. The background
window capture asks each process for its `WM_GETICON` or class icon and falls
back to the executable's Shell icon. A bounded process-ID cache prevents the
two-second Dock refresh from repeating native extraction and evicts processes
that no longer own a visible Dock window, preventing stale PID reuse. Native
icon extraction failure is cached as an unavailable image for that process and
cannot abort or clear the desktop-window snapshot. Pinned Start Menu commands
resolve and path-cache their shortcut icons on a background thread only when the
bounded pin list is loaded, not during every Launcher query.

The reader asks the Windows ExtraLarge system image list for file and shortcut
icons, then falls back to `SHGetFileInfo` when that interface is unavailable.
The process executable icon is preferred for live windows, with window/class
icons as a fallback. Every resulting `HICON` is rendered into a validated
192-by-192 BGRA snapshot and each owned
Shell/GDI handle is released. On the UI thread,
`ApplicationIconSourceCache` converts that immutable snapshot once into a shared
WinUI `SoftwareBitmapSource`. The UI first renders the Segoe Fluent fallback in
the reserved icon slot, then asynchronously copies the premultiplied BGRA buffer
into a `SoftwareBitmap` and swaps the native image into the same geometry after
`SetBitmapAsync` completes. Conversion failure is isolated to that image and
leaves the fallback visible; it cannot abort the monitor's window snapshot or
clear taskbar items. Process paths and pixel data remain session-local and are
not written to SeanShell settings or telemetry. At 147,456 bytes per icon, the
existing 128-process and 32-shortcut cache caps bound worst-case raw pixel
storage to roughly 22.5 MiB; normal use retains only active processes and the
user's bounded pin set.

`LauncherPerformanceMonitor` is a Core-owned, thread-safe session diagnostic. It
records the first successful show-to-usable duration once and keeps a bounded
window of the 50 most recent successful provider/ranking durations. Cancelled and
failed searches are not samples. The monitor retains durations only, never query
text, command results, or user identifiers, and does not write telemetry to disk.

`DisplayMonitorService` captures Win32 work areas as immutable monitor snapshots.
The App composition root creates one dock window per snapshot. A window subclass
observes `WM_DISPLAYCHANGE` and schedules a debounced topology capture on the UI
dispatcher. Equivalent snapshots are ignored. When handles, work areas, or monitor
membership change, the App constructs every replacement Dock before shutting down
the previous set. A failed or empty capture leaves the existing set active. Window
snapshots carry the nearest monitor handle, allowing each dock to filter locally
without opening or retaining process handles.

`TaskbarReplacementSession` owns the fail-safe Companion Taskbar transition. It
requires `ITaskbarRecoveryGuard` readiness before calling the Windows-only
`ITaskbarController`. `WindowsTaskbarController` discovers the primary
`Shell_TrayWnd` and all `Shell_SecondaryTrayWnd` windows, applies visibility, and
verifies the result. The exact packaged SeanShell executable runs the guard mode
with only the owner process ID; after acknowledging readiness, the guard waits
for that process to exit and restores every taskbar. The App repeats the hide
check every two seconds while replacement is enabled so an Explorer restart
cannot silently bring the Windows taskbar back over the dock.

The session also exposes a bounded `Reveal` transition that shows native taskbar
windows without disabling the recovery guard or changing the persisted Companion
preference. MainWindow pauses its re-hide timer only after a successful
user-initiated reveal. A second selection or entry into Gaming Mode calls
`EnsureHidden` and resumes the timer. This preserves direct access to Explorer's
notification area without unsupported tray enumeration, global input synthesis,
or shell-process injection.

MainWindow owns one 15-second clock timer and distributes the same local timestamp
to every Dock. Each Dock formats short time and short date through
`CultureInfo.CurrentCulture`, so Windows regional preferences remain authoritative.
Opening the clock flyout sets a native WinUI `CalendarView` to that captured
local date. The timer continues updating the clock label but does not reset a
month the user navigated to while the flyout remains open.

Opening a Dock Quick settings flyout captures network availability and native
power status once through `SystemStatusSnapshotService`. Core formats that
snapshot into visible and accessible text before the flyout is shown. The
capture is demand-driven: no network or battery polling timer remains active
while Quick settings is closed.

The same flyout uses `AudioEndpointService` to activate the current Windows
Core Audio render endpoint. It reads master volume and mute state on opening,
then applies explicit Slider or mute-toggle changes through
`IAudioEndpointVolume`. COM objects are released after each bounded operation;
there is no audio callback, process injection, or background audio poller.
Each Dock captures one initial system and audio snapshot for its compact
network, speaker, power, and battery-percentage cluster. Opening Quick settings
or changing audio refreshes that cluster; no additional status timer is created.

Quick settings and the clock calendar share theme-resource-driven Flyout
presenter, surface, section, action, and icon-tile styles. The styles retain
native control templates and focus visuals while adding one consistent
acrylic-layer hierarchy across light, dark, and high-contrast themes. These
system flyouts opt out of the compact Dock XAML root bounds so their primary
controls and calendar can render at their natural size above the Dock instead
of being compressed into an internal scrolling viewport.

Each Dock owns one `AppBarWorkAreaReservation`. After the recovery guard has
started and native taskbars are hidden, the reservation follows the documented
`ABM_NEW`, `ABM_QUERYPOS`, and `ABM_SETPOS` sequence for that Dock window and
display. The approved rectangle is briefly applied to the native window before
the Dock returns to its centered visual bounds. Windows can then recalculate each
monitor's work area without requiring the native taskbar to remain visible.

The reservation requests the Dock's full DPI-scaled height plus its vertical
margin. It does not subtract the native taskbar's transitional inset. Revealing
the system area, entering Gaming Mode, disabling replacement, rebuilding Docks,
and shutdown all remove the reservation first. Destroying a Dock HWND also makes
Windows discard its AppBar registration after a forced exit.

While replacement is active, SeanShell observes native foreground, minimize,
virtual-desktop switch, visibility, destruction, location-change, and title-change
events. Bursts are
debounced for 120 milliseconds before the Dock inventory and eligible visible
window presentations are captured; two-second fallback scans cover applications
that omit expected accessibility events. The event-driven inventory invalidates
its short-lived snapshot cache before capture so taskbar state never reuses the
pre-event window list. A maximized window or a borderless window whose extended
frame covers its monitor enters per-display immersive mode. Each
affected display releases its AppBar reservation and collapses its Dock to the
bottom-edge reveal target; other displays retain their normal reservations.
Because this state is based on all visible, non-cloaked windows rather than only
the foreground HWND, moving focus to another display does not resize an immersive
window left behind. Returning every window on a display to ordinary mode restores
that display's reservation and expanded Dock without relying on undocumented
virtual-desktop APIs.

Each Dock also keeps a monitor-local, session-scoped order for running window
groups. Surviving groups retain their visual position across event-driven
snapshots, newly observed groups append at the end, and closed groups are removed.
Users can drag visible running groups into a preferred order for that display;
keyboard and assistive-technology users can invoke equivalent Move left and Move
right context actions. The next event-driven snapshot preserves the manual order.
The order deliberately resets when a Dock is rebuilt rather than persisting stale
window identities across sessions.

Window capture queries the public `IVirtualDesktopManager` COM interface before
adding a taskbar candidate, so each Dock reflects only the currently active
virtual desktop. The query is fail-open: unavailable COM activation, an ABI-format
mismatch, or a failed per-window HRESULT retains the candidate, while the existing
DWM cloaking filter and fallback reconciliation continue to provide compatibility
coverage. SeanShell
does not use undocumented virtual-desktop switching or notification interfaces.
The documented `EVENT_SYSTEM_DESKTOPSWITCH` WinEvent feeds the same debounced
inventory pipeline, so changing desktops does not wait for fallback polling.

Taskbar visibility can settle asynchronously, especially on a secondary display.
`WindowsTaskbarController` reissues the requested transition for a bounded
two-second window and fails safe to the native taskbar if the final state still
does not match.

## Reliability boundaries

- Explorer remains the shell-service and recovery fallback in Overlay and
  Companion Taskbar modes.
- Plugin operations are asynchronous and cancellable. Initialization, command
  queries, suspend, resume, and disposal are bounded by host timeouts. A failed
  plugin transitions to a session-local faulted state while healthy plugins keep
  serving commands.
- Git integration is read-only. It may open a repository in Explorer, VS Code, or
  Windows Terminal, but never runs pull, commit, checkout, reset, or clean.
- WSL integration caches the output of `wsl.exe --list --verbose`. Enumeration
  runs only during initialization or an explicit refresh. The plugin may start a
  selected distribution by opening its shell or files, but exposes no terminate,
  unregister, import, export, default-change, or arbitrary command actions.
  Docker Desktop's internal distributions are filtered from Launcher commands.
- Docker integration caches `docker container ls --all` JSON lines during plugin
  initialization or an explicit refresh. A missing CLI or offline Engine is a
  recoverable availability state, not a plugin fault. Container commands may open
  a running container's published localhost TCP port or follow logs in a separate
  console, but expose no start, stop, restart, exec, remove, pull, or Compose
  mutations.
- .NET workspace integration only opens a cached solution/project path, its
  containing folder in VS Code, or Windows Terminal, or starts an exact visible
  `dotnet build`, `dotnet test`, or `dotnet run --project` command after the user
  selects it. Runnable projects may also start `dotnet watch run` for hot reload
  or open a validated loopback launch-profile URL. Arguments are passed directly
  without a command shell. Package restore and project mutation commands are not
  exposed.
- Only built-in instances registered by the App composition root are accepted.
  The external catalog scans at most 32 immediate package directories and reports
  manifest, containment, content-hash, Authenticode, and publisher-fingerprint
  diagnostics. Whole-chain publisher revocation failures are fail-closed and
  surfaced separately from revoked or expired certificates. It never loads
  candidate code. External execution remains blocked after consent until
  capability-restricted broker activation and out-of-process isolation are
  implemented.
- The production broker client resolves the current packaged executable through
  `Environment.ProcessPath`; no user-configurable executable path participates
  in App composition. It starts that exact path without a command shell, redirects
  only standard input/output, validates the response against the started process,
  and applies a two-second timeout. It creates the process with its primary
  thread suspended and an explicit inheritance list containing only the three
  redirected standard-stream handles plus a private session-key pipe. It then
  assigns the process to a
  one-process, 256 MiB Windows Job Object configured to kill its members when
  closed before resuming the primary thread. The broker disables legacy
  extension points, remote and low-integrity image loading, and child-process
  creation before reading stdin. Any setup failure terminates the still-suspended
  process, closes the job, and fails the request. Each launch receives a random
  256-bit key before resume; both request and response are HMAC-authenticated,
  correlated to the same session/nonce, and key buffers are cleared afterward.
  `SeanShell.PluginBroker.Runtime` also owns an inactive, collectible dependency
  load context for the future activation path. It admits only runtime framework
  assemblies, explicitly supplied shared contracts, and exact-hash manifest
  dependencies. Managed dependencies load from the verified open stream; undeclared
  managed/native requests throw instead of falling through to host or platform
  search. No current broker operation constructs this context.
  The protocol assembly separately defines strict bounded command DTOs and a
  canonical descriptor-set digest. They contain only display text, opaque IDs,
  fixed outcomes, and hashes; no enabled request/response references them.
  The preview broker handles one bounded frame and exits. Before a
  metadata probe, the host repeats catalog trust and consent checks and sends a
  grant valid for 15 seconds. The broker accepts at most 30 seconds, rejects
  unknown capabilities, traversal and reparse points, and recomputes SHA-256.
- External manifests may allowlist at most 32 managed/native dependency DLLs.
  The host requires canonical package-relative paths, bounded individual and
  aggregate size, declared SHA-256, Authenticode trust, and the same publisher
  certificate as the entry assembly. The broker independently repeats
  containment, reparse, size, and hash validation and returns only count plus a
  canonical dependency-set digest. No dependency is loaded.
- `PluginBrokerQuarantineManager` persists a separate schema-1 broker-health
  document. Three counted failures for one external plugin within ten minutes
  block new probes for thirty minutes; a successful probe clears the window.
  Corrupt history recovers from its last-known-good copy or blocks every probe.
  User cancellation, trust failure, and a missing broker binary are not charged
  to a plugin.
- Configuration writes will be atomic and recover from a last-known-good copy.
- Gaming mode pauses polling and animations; it never disables security services,
  injects code, hooks rendering, or intercepts game input.
- The dock lists ordinary visible top-level application windows, excludes
  SeanShell itself, and uses `SetForegroundWindow` only after a user selection.
- Automatic startup is an opt-in packaged `windows.startupTask` registration.
  The dashboard reads the live Windows task state and never shadows it in the
  settings document. Windows policy and a user's Task Manager choice take
  precedence. Rich `StartupTask` activation and the development-only `--startup`
  mode use a persistent crash-loop guard and exit after three consecutive
  incomplete startup windows. Automatic activation leaves the Dashboard hidden
  after constructing the monitor-local Docks; the Dock's Dashboard action restores
  it normally. Manual launches remain available for recovery.
- Before WinUI starts, the process uses Windows App SDK app lifecycle to register
  the stable `SeanShell.Main` instance key. A later process redirects its activation
  arguments to the registered process and exits before creating UI. The primary
  process restores its existing Dashboard for redirected manual launches and
  ignores redirected startup-task foreground requests, preventing duplicate Docks
  without making sign-in startup intrusive.

## Configuration

`ShellSettingsStore` owns a versioned JSON document at
`%LOCALAPPDATA%\SeanShell\settings.json`. It writes a sibling temporary file,
flushes it to disk, and replaces the primary document while retaining
`settings.json.bak`. Invalid JSON, unknown schema versions, and unsupported
shortcut values never reach the UI: the store loads the backup or safe defaults
and returns a warning for the dashboard.

Schema version 8 persists Dock auto-hide, the opt-in Companion Taskbar preference,
up to eight ordered pinned application IDs, one of three reviewed Launcher shortcuts,
one of two reviewed Dock-focus shortcuts,
opt-in automatic game detection,
newline-delimited game process rules, normalized disabled plugin IDs, appearance,
and display density. Versions 1 through 7 migrate in memory without losing
existing preferences; taskbar replacement always defaults off when migrating.
Arbitrary key capture is intentionally excluded so SeanShell never needs a
keyboard hook. Each shortcut change is committed only after `RegisterHotKey`
succeeds; failed registration restores the previously active shortcut. Every
`GlobalHotKey` instance owns a distinct native registration and window-subclass
identifier so Launcher and Dock shortcuts can coexist on the main HWND.

The Dock shortcut captures the current foreground HWND and its nearest monitor.
`DockTargetMonitorResolver` selects that monitor's Dock, then falls back to the
primary monitor and finally the first available monitor. The App expands the
selected Dock, activates it through the same validated foreground-switch boundary
used by taskbar items, and places keyboard focus on the Launcher button. Escape
restores and activates the captured window when it is still valid, then immediately
collapses an auto-hidden Dock. If the display topology changes, the next invocation
uses the rebuilt monitor and Dock collections rather than retaining a stale Dock
HWND.

Plugin enablement is independent of Gaming Mode. Disabling a plugin suspends it
and removes its Launcher commands; disabling before startup skips initialization.
Enabling while Gaming Mode is active initializes or resumes the plugin and then
keeps it suspended until normal mode returns. A failed settings write rolls the
runtime state back so persisted and visible state remain consistent.

External consent is independent from built-in enablement. A schema-1
`plugin-trust.json` document binds the package ID, signer certificate SHA-256,
exact capability flags, and UTC grant time. The trust manager saves a complete
replacement document before changing its in-memory snapshot, so a failed write
cannot create session-only approval. Primary corruption recovers from `.bak`;
unrecoverable data fails closed to no approvals. Removing a package does not
remove the decision, so the dashboard also provides a package-independent
revoke-all action.

`StartupCrashLoopGuard` owns a small schema-1 `startup-health.json` document under
the App's local SeanShell data directory. In an MSIX launch, Windows redirects
this to the package's `LocalCache\Local\SeanShell` directory; unpackaged launches
use `%LOCALAPPDATA%\SeanShell`. A new session is pending until the main window
closes normally or the process remains alive for 30 seconds. A later automatic
launch treats an earlier pending session as one startup failure. After three
consecutive failures, only `--startup` launches are blocked; manual launch is
always allowed and a healthy manual session resets the counter. If health state
cannot be persisted, automatic launch fails closed while manual recovery remains
available.

When an automatic launch is blocked, the Windows boundary ensures Explorer is
running before SeanShell exits. `tools/restore-explorer.ps1` provides the same
recovery path, requests a graceful close, stops only a still-running
`SeanShell.App` process after two seconds, and removes both packaged and
unpackaged health-document locations. The guard does not change Winlogon,
Scheduled Tasks, registry Run keys, or the configured Windows shell.

`GamingModeManager` combines two independent sources: a session-only manual
override and the active process matches produced by automatic detection. Effective
gaming mode remains active while either source is active. This prevents a game
exit from cancelling a manual override, and prevents disabling the manual toggle
from cancelling a still-running detected game.

Automatic detection takes one disposable process-list pass every two seconds and
retains snapshots only for configured executable names. Its performance monitor
keeps at most 60 samples and exposes scan P95 plus estimated detector CPU use to
the dashboard. The estimate is diagnostic evidence for representative sessions,
not a system-wide profiler.

`GamingSessionRecorder` observes detected-process transitions independently from
the manual Gaming Mode override. It starts a session only when at least one
configured executable is detected, completes it after the final match exits, and
retains the 20 newest records through an atomic, backup-backed schema-1 JSON
store. Records remain local and contain process names and compatibility metadata,
not game content or process handles.

## Deployment

The initial app uses single-project MSIX packaging and a debug identity generated
by the Windows App SDK tooling. The package manifest enables content-integrity
enforcement, so Windows checks and repairs tampered package contents before
launch. Single-project MSIX permits one application executable, so the signed App
binary also hosts the broker mode while its runtime remains UI-independent.
Production certificate management, release signing, update channels, and
full-shell policy are deferred until the MVP has measured compatibility data.
