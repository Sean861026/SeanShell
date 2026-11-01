# Input layout QA probe

This temporary, standalone Windows Forms app observes keyboard-layout changes in
its own window. It displays the original/current layout handles and a change
count; it does not install layouts, change the default language, save input, or
switch layouts on its own. It closes automatically after five minutes.

## Notebook-only startup on this workstation

```powershell
dotnet run --project tools/SeanShell.InputLayoutProbe/SeanShell.InputLayoutProbe.csproj -c Release -- --notebook-only
```

`--notebook-only` requires exactly one active physical monitor whose device
interface identifies `SDC41A3`. It resolves the monitor before creating a Form,
uses manual placement wholly inside its work area, and rechecks placement before
first visibility and after initial DPI/layout sizing. Missing, ambiguous, or
unusable targets cause exit code 2 without falling back to the primary display.
Unknown arguments produce exit code 64.

All visual QA and interactions must remain on that built-in panel, as required by
the repository's `AGENTS.md`. The option constrains startup; do not drag the probe
to an external monitor.

Focus its text box, then use SeanShell's Dock quick settings to request a layout
change for this test window. Confirm the displayed current handle and change
count. Restore the original layout before closing; the probe deliberately does
not automate restoration or change the user's keyboard settings.

Without arguments, the existing generic centered-window behavior is unchanged.
