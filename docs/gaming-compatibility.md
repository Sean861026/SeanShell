# Gaming compatibility

## Safety contract

SeanShell automatic game detection reads the public Windows process list and
compares process names selected by the user. It does not:

- open or retain game process handles
- inject DLLs or code
- hook DirectX, Vulkan, OpenGL, keyboard, or mouse input
- draw an in-game overlay
- change process priority, affinity, security services, or anti-cheat state

The process snapshot is discarded after each comparison. Matching is
case-insensitive and strips an optional `.exe` suffix.

## Recommended rules

Add the executable for the actual game, not its launcher. For example, prefer
`eldenring.exe` over `steam.exe`. A launcher rule can keep gaming mode active after
the game closes because launchers often remain running.

Automatic detection is disabled by default. Manual gaming mode remains available
for games whose executable name changes or that launch through several processes.

## Manual compatibility check

For each game under test, record:

| Check | Expected result |
| --- | --- |
| Start matching game | Dock hides and dashboard sampling pauses within four seconds |
| Launcher remains open | Gaming mode follows the game executable, not the launcher |
| Exit matching game | Workspace returns within four seconds unless manual mode is on |
| Enable manual mode | Gaming mode stays active after the game exits |
| Disable manual mode while game runs | Automatic detection keeps gaming mode active |
| Anti-cheat launch and play | No warning, kick, or blocked launch attributable to SeanShell |

Compatibility results should include the game version, anti-cheat provider,
Windows build, GPU driver, and SeanShell commit. A successful test is evidence for
that configuration only, not a guarantee for future anti-cheat updates.

## Known limitations

- Rules match process names, not file signatures or product metadata.
- A game that replaces its executable during an update may require a rule change.
- Detection and restoration can take up to one polling interval, currently two
  seconds under normal conditions.
- This milestone does not change Windows Game Mode or power plans.
