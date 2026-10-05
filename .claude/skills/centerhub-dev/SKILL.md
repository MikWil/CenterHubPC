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
| Quiet smoke of a real build (no mouse/keyboard) | `powershell -ExecutionPolicy Bypass -File tools/smoke-quiet.ps1 -Exe <exe> -OutDir <dir>` |
| Headless page render (no window) | `dotnet run --project tools/page-render -- <outDir>` then Read the PNGs |
| Silent real-audio timing check | `dotnet run --project tools/page-render -- <outDir> audio` (exit 0 = pass) |
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
- UIA tests need the foreground: if the user is using the PC, Windows blocks `SetForegroundWindow`
  and clicks land on their app. Don't run UI tests (and never `-Voicemeeter`, which closes Banana)
  while the user is active — prefer non-destructive probes that call the real services directly.
- A modal overlay (What's new / audio prompt) blocks every click; if a UI test can't find a page,
  screenshot before assuming an app bug.

### Verifying without disturbing the user
- The user usually has the *installed* CenterHub running (`%LocalAppData%\CenterHub`). A Debug build
  can't run beside it (single-instance mutex — it just pops their window), and killing theirs drops
  their audio routing. Use `tools/page-render` instead: it hosts a view + real view-model in
  Avalonia.Headless with the app theme and saves PNGs. Extend it for other pages the same way
  (construct the VM with fakes / temp storage folders; never the DI host).
- Headless caveats: `DispatcherTimer`s never fire (tick handlers via reflection), and there is no
  `Application.DataTemplates` — set `Content = new XView { DataContext = vm }` directly. After
  `SetupWithoutStarting()` the main thread has Avalonia's synchronization context and nothing pumps
  it: `SomethingAsync().GetAwaiter().GetResult()` deadlocks. Run async checks via `Task.Run` or
  before the Avalonia setup.
- Give any check that can leave the machine in a bad state (Banana down) a per-step timeout and a
  final "put it back" step, and write its output to a file — a piped `Select-Object -Last` shows
  nothing until the process ends, so a hang looks like silence.
- Anything audible is verified at `MasterVolume = 0` on the real device (`audio` mode) plus offline
  rendering in unit tests. Never play test audio through the user's speakers.

## Release (signed MSI + zips + GitHub release)

1. Bump `<Version>` in `CenterHubNew.csproj` (semver; 6.0.0 not 6.0).
2. Run `.\build-installer.ps1 -Configuration Release` (builds, signs exe + MSI, stamps
   `installer/Package.wxs`, makes `CenterHub-vX.zip` and `-Portable.zip`). The installed app lives
   in `%LocalAppData%\CenterHub` and does not lock the repo's output — don't kill it; only a
   Debug/Release instance started from the repo does.
2b. Smoke the *published* exe before publishing: `tools/smoke-quiet.ps1 -Exe .\publish\Release\CenterHubNew.exe`.
   It sets `CENTERHUB_DEV_INSTANCE=1` (own single-instance scope, see `Program.cs`) so it runs
   beside the installed app, opens every page via UIA `SelectionItemPattern` (no input, no
   foreground) and screenshots with `PrintWindow`. Read the PNGs.
3. Copy `installer/bin/x64/Release/CenterHub.msi` → `CenterHub-vX.Y.Z.msi` (release asset name).
4. Stage everything **except** `AGENTS.md` (untracked dev-guide copy), commit `Release vX.Y.Z — …`.
   Commit with `git commit -F <message file>` (or from the Bash tool): Windows PowerShell 5.1
   mangles double quotes inside a `-m` here-string, the commit fails, and anything chained after it
   with `;` still runs — in v6.1.0 that tagged and pushed the *previous* commit. Run commit, tag
   and push as separate steps and check `git rev-parse vX.Y.Z^{commit}` equals `HEAD` before pushing.
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
- **`ScrollViewer.Padding` is not subtracted from the content's measure width** (11.2): content
  laid out at full width and shifted by the padding → clipped on the right. Put the side padding
  as a `Margin` on the child inside the scroller (see `PART_BodyContent` in Controls.axaml).
- **TextBlocks inside a ControlTemplate are hidden from UI Automation** (`TemplatedParent != null`
  → not a control element). Anything a UIA test must find (page titles) goes through a
  `ContentPresenter` + `DataTemplate`, whose TextBlock has no templated parent. Also: the global
  `TextBlock` style beats inherited `FontSize`, so set font props on the TextBlock itself.
- **A class-only selector (`.foo`) does not compile** in `Style Selector` — write `:is(Control).foo`.
- `Slider` snap property is `IsSnapToTickEnabled` (not WPF's `IsSnapToTick`).
- **Theme tokens must be `{DynamicResource}`** — `StaticResource` resolves once, so a theme or
  accent switch leaves that element on the old palette. Headless renders catch brush
  *transitions* mid-way after a live theme switch (greyish buttons) — not a bug.
- Verify page reflow headlessly: `dotnet run --project tools/page-render -- <out> widths` renders
  Metronome at 480/640/900/1300 px + light theme and prints root width / `:narrow`;
  `… palette` renders the command palette. In-app sizes: `tools/smoke-quiet.ps1 -Sizes "1100x800,640x760"`.

## Windows PowerShell 5.1 file editing

- **Never round-trip source files through `Get-Content` / `Set-Content`**: without a BOM PS 5.1
  reads UTF-8 as Windows-1252, and writing back turns `—`, `·`, `↻` into `â€"`, `Â·`, `â†»`
  (shipped visibly on the Sound page once). Use the Edit tool, or
  `[IO.File]::ReadAllText` / `WriteAllText(…, new UTF8Encoding($false))`.
- PS 5.1 also reads a `.ps1` without BOM as ANSI, so non-ASCII literals in a script break —
  use `\uXXXX` in regexes or save the script with a BOM.
- A crashed app instance can linger as a dead process (0 threads, "access denied" to kill) that
  still locks `bin\…\CenterHubNew.exe/.dll`. Renaming a locked image is allowed: move the files
  aside (`*.stale-N`) and build again.

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
- `VBVMR_Login` returns 0 (running), 1 (installed, not running) or **-2 (this client is already
  logged in)**. Treating -2 as failure caused "Could not start Voicemeeter Banana" after Banana was
  closed. Always log in via `LoginLocked()` (logout + retry on -2), and when the keep-alive sees
  the engine gone, really `Logout()` — never just clear the flag.
- **The Remote API lies after a crash/kill**: `GetVoicemeeterType` keeps returning 2 from stale
  shared memory (for as long as any client still has it open), sets "succeed", device names read
  back fine — with no process. "Running" therefore means *engine process exists* AND type > 0, and
  "ready" additionally needs the process to be ≥ 1.5 s old (a fresh instance otherwise looks ready
  through the old instance's data and loses the settings sent to it). See `IsEngineProcessAlive`.
- Measured on a healthy Banana: exits ~1 s after `Command.Shutdown`, engine answers ~0.4 s after
  launch, and a session kept across the restart re-attaches by itself. Restart = polite shutdown →
  wait → kill only if still there → relaunch → wait ready. Kill by exact engine exe names, not a
  `voicemeeter*` prefix (that also hits VoicemeeterMacroButtons).
- The keep-alive must stand down during a start/restart (`_lifecycleDepth`); it used to log the
  session out mid-wait. Every Remote DLL call goes through `_gate`.
- **Setting a device is a request, not a fact.** Read back `Bus[0].device.name`: A1 was found
  empty ("-") and once pointed at "Voicemeeter In 2" (its own input). `ApplyPresetAsync` verifies
  A1, retries, and reports; the setup check has a "Headphone output" item; device pickers hide
  Voicemeeter's own endpoints. Only assign a device Banana doesn't already have (each assignment
  reopens it = dropout). Use the wide (`…StringW`) calls — device names are non-ASCII here
  ("Högtalare (PRO X 2 LIGHTSPEED)").
- **Banana opens a WDM output in exclusive mode.** While it holds the headset as A1, any app
  playing straight to the headset gets `AUDCLNT_E_DEVICE_IN_USE` (0x8889000A) = silence. Games
  must output to "Default" / a Voicemeeter input. (MME on A1 would share, at higher latency.)
- **Headphones are shared by default** (`VoicemeeterSettings.ShareMonitorDevice`, the "Other apps
  can use it too" checkbox in Setup): shared = `Bus[0].device.mme` (name cut to 31 chars),
  exclusive = `.device.wdm`. Banana can't be asked which driver it uses, so
  `IAudioDeviceService.IsPlaybackDeviceLocked` looks at the effect (a never-started shared
  `AudioClient.Initialize` fails with DEVICE_IN_USE when locked) and `EnsureMonitor` reopens the
  device only when the mode is wrong. The user chose shared; never switch A1 back to WDM silently.
- Only the *output* is locked: the headset mic can be captured directly while Banana runs
  (measured). **Direct mode** (`AudioRoutingService.BypassAsync`, the "Direct (no Banana)" chip)
  is the bypass: `ShutdownAsync` closes Banana, then the configured headphones + mic become the
  Windows defaults and `SessionActive` is cleared so the startup prompt stays quiet. Applying a
  preset leaves it. Apps pinned to a Voicemeeter slot in Windows stay silent while Banana is off.
- **NAudio + AudioSwitcher share one COM enumerator object.** If AudioSwitcher wraps it first
  (generic RCW), every later `new MMDeviceEnumerator()` throws InvalidCastException for the rest of
  the process → empty device lists, "virtual devices are missing". `AudioDeviceService` holds a
  NAudio enumerator for its lifetime (`_comAnchor`) so the typed wrapper wins. Never create a
  `CoreAudioController` yourself: use `AudioDeviceService.SharedController` (one per switch leaked
  and made each switch slower: 1.6 s → 5.7 s; shared = ~25 ms).
- Apply / re-sync / restart are serialized in `AudioRoutingService` (`_applyGate`); two at once
  hung inside the default-device switch.
- Real-Banana check: `dotnet run --project tools/page-render -- <out> voicemeeter [quick]`
  (restarts Banana up to ten times: normal, killed behind our back, suspended = hung, concurrent).
  `com-probe` checks device enumeration after a default switch without changing anything. Only run
  `voicemeeter` when the user asked for it, and always confirm Banana is running afterwards.
- With the Sound board, routing engaged at exit is normal. The startup prompt only appears if
  Banana is still down ~12 s after launch (it often starts alongside CenterHub at sign-in).
- Recovery ladder: **Re-sync** (reconnect + `Command.Restart` engine + reapply) → **Restart
  Voicemeeter** (`Command.Shutdown`, force-kill `voicemeeter*`, relaunch, reapply). A hung Banana
  ignores the polite shutdown — the kill is required.
- Banana strips: mic 0, guitar 1, line-in 2, VAIO 3, AUX 4 (VAIO3 5 on Potato); A1 = monitor,
  B1 = Discord send. **Desktop & Discord (VAIO) must never route to B1** (echo) — enforced in
  `AudioRoutingService` for both `ApplyPresetAsync` and `LiveSet`, and unit-tested.
- Discord must have Input **and** Output = Default or it ignores the routing (most "it doesn't
  work" reports). The setup check (`RunChecks`) reminds when Discord is running.
- Per-app output (`AudioPolicyConfig`, EarTrumpet approach) needs classic COM on .NET 5+ (no
  HSTRING/IInspectable marshaling) and its vtable shifts between Windows builds. **On build 26200
  the call returns success and moves Windows' MAIN output to the slot** (measured with
  `tools/page-render … perapp-probe`) — every app then plays into the slot, and a slot sent to
  Others put the user's Chrome tab in Discord. So: `CanAssignAppsInApp` is false on builds ≥ 26200
  (picker hidden, Windows button only); on other builds `AssignAppToSlot` compares the main output
  before/after, undoes a move and disables itself for that build (persisted). Never trust an
  undocumented call's return value — check the effect. Re-resolve the app's PID by name at assign
  time — PIDs get recycled (Spotify's PID once routed Chrome).
- **Windows' main output must never be a slot input** (AUX / VAIO3 / "Voicemeeter In N" / VB-Cable):
  `AudioRoutingService.KeepDesktopOutputAsync` moves it back to "Voicemeeter Input" (the Desktop &
  Discord row, never sent to Others) at startup and whenever `IAudioDeviceService.DefaultPlaybackChanged`
  fires, while Banana runs; a real device (direct mode) is left alone. `default-guard` in
  `tools/page-render` exercises it on the real machine.
- Banana has only 2 virtual inputs; a 3rd app slot needs VB-Cable on spare strip 2.

## Metronome / drum machine

- Timing lives in the audio callback, never in a UI timer: `DrumMachineEngine.Read` counts samples
  (48 ticks/beat, fractional remainder carried) — the old `DispatcherTimer` click drifted and
  jittered. UI lights come from engine events stamped with the frame they start on, released by
  `MetronomeService` when the device position passes that frame.
- **WaveOutEvent with 2 buffers / 80 ms under-runs on Voicemeeter's virtual device** (measured:
  device consumed 39–43.8k of 44.1k frames/s → gaps and a dragging tempo; no exception, no log).
  Use WASAPI shared + event sync (50 ms), WaveOut 100 ms / 3 buffers only as fallback.
  `tools/page-render … audio-diag` measures "played seconds per second" per configuration — run
  it before touching output settings; anything under ~0.999 is starving.
- Device position is in bytes of the *device* format: convert via `OutputWaveFormat.AverageBytesPerSecond`,
  not the engine's block align.
- Styles are drum tabs (`DrumBar.Parse`). A line with the wrong number of steps throws when the
  library is first touched (i.e. opening the Metronome page) — `DrumStyleLibraryTests` must pass
  after any pattern edit. Engine tests render offline and assert exact frames; keep them exact.
- Events are delivered when audible, so a `Stopped` event from the previous run can arrive after a
  new `Start()` — the view-model ignores it while the engine is playing.
- **Looper**: engine tests with an attached-but-empty looper must stay bit-identical to no looper.
  The real capture path can't be unit-tested; check it silently with
  `dotnet run --project tools/page-render -- <out> looper-probe` (drums at volume 0, records one
  bar from the real input, verifies length and that the loop has no holes). It writes a WAV of
  the user's input — delete it afterwards. How good the loop *sounds* (timing after calibration)
  only the user can judge.
- **"Robotic" loop = the capture clock chasing jitter** (7.1.0): `CaptureClock` followed the jittery
  device position one frame per buffer, so 3 of 4 buffers dropped/repeated a sample (measured 535
  of 701). Rules now: fixed offset after an 8-buffer warm-up, a 2 s settle that glides ≤ 6
  frames/buffer (the first estimate is 40–170 frames off while the output spins up), then a 32-frame
  deadband with hysteresis and ≤ 1 frame per 8 buffers — which only tracks real drift between the
  two devices' clocks (Katana vs output ≈ 3 frames/s). Every correction is *stretched* into the
  buffer by `LooperEngine.WriteInput` (never drop/repeat/hole). The probe prints the counts and the
  clock error over time; `looper-probe KATANA` probes a device by name.
- Never fold stereo to mono by picking the louder channel per sample (distortion) — average the
  channels that carry signal.
- **"Real drums" (`DrumKitKind.Acoustic`, the default)** plays recorded hits from
  `Assets/Drums/acoustic.chdk` (embedded resource; public-domain Open Source Drumkit, see
  `Assets/Drums/README.md`; rebuild with `tools/page-render … build-drumkit`). Velocity picks the
  hit (recordings carry their own dynamics — no `velocity^1.5`), neighbouring hits alternate so a
  repeated note never sounds identical, and humanized pattern hits start 0–6.4 ms late. The user
  called the synthesized kits "robotic"; that was the fix. Clap/cowbell/tambourine/shaker/sticks
  are not recorded and stay synthesized.
- Nobody here can listen: judge sound changes with `tools/page-render -- <dir> render-demo`
  (offline WAVs through the real engine) and send them to the user before releasing.

## Working with sub-agents here

- Parallel read-only reviewers (services / view-models) found most real bugs in this pass — worth
  doing before a release. Verify their high-severity claims in code before acting.
- Give implementers disjoint file lists and tell them **not to build** while others edit; build
  and review once, centrally.
