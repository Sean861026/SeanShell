# Input layout switching validation

Quick settings > **Input methods** exposes the loaded HKL keyboard layouts for
the previously focused external application. It is not a complete TSF profile
picker or an IME Chinese/English conversion switch. The Windows-native indicator
and keyboard settings remain available for those cases.

## Automated checks

`InputLayoutSwitchControllerTests` covers own-process rejection, invalid/closed
windows, HWND reuse with a changed process/thread, foreground restoration failure,
removed layouts, already-active layouts, message rejection, and the difference
between a posted request and an observed change. Enumeration is bounded to 64
nonzero, unique runtime layout handles.

The native adapter revalidates foreground ownership and the focused child HWND
before posting `WM_INPUTLANGCHANGEREQUEST`. It never broadcasts, calls SendInput,
attaches threads, loads new layouts, modifies default language settings, or hooks
keyboard input. Windows UIPI and the receiving app may deny or ignore the request.

## Isolated manual probe

Run from the repository root:

```powershell
dotnet run --project tools/SeanShell.InputLayoutProbe/SeanShell.InputLayoutProbe.csproj -c Release
```

The probe is a temporary blank WinForms application. It displays its original
layout, current layout, and observed `InputLanguageChanged` event count. It saves
no text/files/settings and auto-closes after five minutes. Do not test against
an active game, a terminal, or an application containing unsaved user data.

1. Note **Original** and **Current** in the probe.
2. With its empty input box focused, press the configured Dock shortcut
   (default `Ctrl + Alt + D`).
3. Open Quick settings > Input methods. Verify the original layout is checked.
4. Choose a different loaded layout. The probe should regain focus, show a
   changed **Current**, and increment its event count.
5. Reopen Quick settings. Success should be reported only after observation;
   an ignored/denied request must show the native-indicator fallback message.
6. Choose the original layout and verify **Current = Original** before closing.

On the development machine, the initial live check observed
`04040404 -> 04090409 -> 04040404` with exactly two input-language events and
focus returned to the probe each time. These are runtime HKL values, not durable
keyboard identifiers. No persistent language/IME settings were changed.

Modern TSF profiles, elevated applications, multi-threaded hosted input controls,
and IME conversion-mode behavior still require native fallback/compatibility
testing. No claim of universal input-method support is made.
