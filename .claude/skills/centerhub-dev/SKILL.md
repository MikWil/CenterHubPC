---
name: centerhub-dev
description: How to build, run, test (unit + UI regression), and release CenterHub (Avalonia 11 / .NET 10 Windows app), plus the hard-won gotchas — Avalonia styling/templating traps, global-hotkey key codes, Voicemeeter Remote API + per-app audio interop, DI lifetimes, tray/minimize behaviour. Use for any change to CenterHub, before claiming a fix works, and whenever cutting a release.
---

# CenterHub development skill

Learned while polishing CenterHub into v6.0. Follow it before changing code, before saying
something "works", and when releasing.

## Build, run, test

| Task | Command |
|---|---|
| Build (must be 0 warnings) | `dotnet build -c Debug -p:Platform=x64` |
| Unit tests | `dotnet test tests/CenterHubNew.Tests/CenterHubNew.Tests.csproj` |
| UI regression (asserts behaviour) | `powershell -ExecutionPolicy Bypass -File tools/regression.ps1` |
| UI screenshots of every page | `powershell -ExecutionPolicy Bypass -File tools/ui-smoke.ps1 -OutDir $env:TEMP\smoke` then Read the PNGs |
| Kill a stale instance (locks the exe) | `taskkill //IM CenterHubNew.exe //F` |

- A build fails with *"file is locked by CenterHubNew (PID)"* when the app is running — kill it first.
- **Verify, don't guess.** Every UI fix gets a screenshot or a regression assertion. Twice in this
  project a "fix" was wrong (ShowInTaskbar duplicates, storage bar) and only pixels/UIA proved it.
- `tools/regression.ps1` backs up and restores `%AppData%\CenterHub\quick-notes.json`; any new test
  that writes user data must do the same.
- Unit tests must never touch the real `%AppData%`: services that persist take an optional storage
  folder (e.g. `new VoicemeeterSettingsService(null, tempFolder)`), and `tests/.../Fakes.cs` has
  `FakeVoicemeeter` / `FakeAudio` / `RoutingHarness`.

### Driving the UI with UI Automation
- Sidebar items are `RadioButton`s named by their label; page headings are `Text` elements in the
  content area (x > window.x + 230). Assert the heading appears — "clicked" is not "navigated".
- Click like a human: move, settle ~150 ms, press, release. Teleport-and-click races the previous
  item's hover/tooltip and drops clicks.
- `SelectionItemPattern.Select()` behaves like keyboard selection — useful to test that keyboard
  navigation works, not a substitute for a click.
- Avalonia `TextBox` supports `ValuePattern.SetValue`, which fires the TwoWay binding.
- PowerShell: never name a variable `$pid` (read-only automatic variable).

## Release (signed MSI + zips + GitHub release)

1. Bump `<Version>` in `CenterHubNew.csproj` (semver; 6.0.0 not 6.0).
2. `taskkill` the app, run `.\build-installer.ps1 -Configuration Release` (builds, signs exe + MSI,
   stamps `installer/Package.wxs`, makes `CenterHub-vX.zip` and `-Portable.zip`).
3. Copy `installer/bin/x64/Release/CenterHub.msi` → `CenterHub-vX.Y.Z.msi` (release asset name).
4. Stage everything **except** `AGENTS.md` (untracked dev-guide copy), commit `Release vX.Y.Z — …`.
5. `git tag -a vX.Y.Z`, push `master` and the tag.
6. `gh release create vX.Y.Z --title "vX.Y.Z — …" --notes-file … <msi> <zip> <portable zip>`.
7. Existing installs auto-update from GitHub Releases — only publish what passed tests.

## Avalonia 11 gotchas

- **Fluent accent follows the Windows accent** (e.g. purple). Pin it in `App.axaml`:
  `<FluentTheme><FluentTheme.Palettes><ColorPaletteResources x:Key="Dark" Accent="#FF4CC2FF"/>…`.
- **Fluent controls paint state on template parts.** Styling `ToggleButton:checked { Background }`
  does nothing; target `ToggleButton.x:checked /template/ ContentPresenter#PART_ContentPresenter`.
- **Custom button templates must not name their presenter `PART_ContentPresenter`** — Fluent's
  hover/pressed/disabled theme styles then paint a box behind the text (the "Stop" bug). Use
  another name (here `ContentHost`).
- **`ProgressBar` mis-sizes its fill** in small fixed-width, frequently rebuilt items. Use a
  track + fill `Border` with `Width` from `PercentToWidthConverter` (param = full width).
- **Don't toggle `Window.ShowInTaskbar` at runtime** — it recreates the native window, re-fires
  `Opened`, duplicates the taskbar button and tray icon. Minimize-to-tray = `Hide()`; restore =
  `WindowState = Normal; Show(); Activate()`. Guard tray-icon creation (create once).
- **Sidebar RadioButtons** bind `IsChecked` TwoWay; keyboard/UIA only flips `IsChecked` without
  running `Command`. Navigation is driven from `OnIsXSelectedChanged` with a reentrancy guard.
- Assigning an `[ObservableProperty]` in a constructor runs its `OnXChanged` hook — set the
  backing field instead when the hook has side effects (toasts, writes).
- `StringFormat` starting with `{` needs the `{}` escape.
- Toasts live bottom-right so they never cover page-header buttons.
- `AVLN3001` (no public parameterless ctor) is suppressed: windows come from DI.

## Architecture rules learned the hard way

- **Anything a global hotkey touches must be a DI singleton** (Sound, Standing, Clipboard,
  AutoClicker, Soundboard VMs). Transient + `GetService` in a hotkey callback = a throwaway VM per
  key press (Standing could never stop; timers/handlers leaked).
- **Avalonia `Key` ≠ Win32 VK.** `Key` follows WPF (Key.K = 54 = VK '6'). Always map through
  `GlobalHotkeyService.KeyToVirtualKey` (unit-tested).
- **Never write user data next to the exe.** MSI installs live in Program Files (not writable,
  wiped on upgrade). Everything persists under `%AppData%\CenterHub\` (hotkeys.json migrated there).
- **All JSON saves go through `AtomicFile.WriteAllText`** (temp + flush + atomic move). On a JSON
  *parse* failure call `AtomicFile.QuarantineCorrupt` before falling back to defaults; never
  quarantine on generic IO errors (a transient lock would hide the user's data).
- Dispose page VMs independently (`SafeDispose`) — Notes saves on dispose and must not be skipped.
- Long work (WLAN/WMI/hardware sampling) runs off the UI thread with a reentrancy guard;
  `SystemMonitorService` serializes sampling and caches < 1 s.

## Voicemeeter / audio routing

- Remote API: load `VoicemeeterRemote64.dll` dynamically from the registry install path; poll
  `IsParametersDirty` ~1/s as keep-alive (the engine drifts when idle).
- Recovery ladder: **Re-sync** (reconnect + `Command.Restart` engine + reapply) → **Restart
  Voicemeeter** (`Command.Shutdown`, force-kill `voicemeeter*`, relaunch, reapply). A hung Banana
  ignores the polite shutdown — the kill is required.
- Banana strips: mic 0, guitar 1, line-in 2, VAIO 3, AUX 4 (VAIO3 5 on Potato); A1 = monitor,
  B1 = Discord send. **Desktop & Discord (VAIO) must never route to B1** (echo) — enforced in
  `AudioRoutingService` for both `ApplyPresetAsync` and `LiveSet`, and unit-tested.
- Discord must have Input **and** Output = Default or it ignores the routing (most "it doesn't
  work" reports). The setup check (`RunChecks`) reminds when Discord is running.
- Per-app output (`AudioPolicyConfig`, EarTrumpet approach) needs classic COM on .NET 5+ (no
  HSTRING/IInspectable marshaling) and its vtable shifts between Windows builds (broken on Insider
  26200). Always keep the "…or in Windows" fallback. Re-resolve the app's PID by name at assign
  time — PIDs get recycled (Spotify's PID once routed Chrome).
- Banana has only 2 virtual inputs; a 3rd app slot needs VB-Cable on spare strip 2.

## Working with sub-agents here

- Parallel read-only reviewers (services / view-models) found most real bugs in this pass — worth
  doing before a release. Verify their high-severity claims in code before acting.
- Give implementers disjoint file lists and tell them **not to build** while others edit; build
  and review once, centrally.
