# CenterHub — Developer Guide

## Project Overview

CenterHub is a Windows desktop productivity app (system monitoring, audio controls, file management, productivity tools). Built with **Avalonia UI 11** + **.NET 10 LTS** following the MVVM pattern. Windows 11 native look-and-feel via Mica backdrop, with Acrylic fallback on older Windows.

## Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET (LTS) | **10.0** |
| Target framework | `net10.0-windows10.0.22621.0` | Win11 22H2+ |
| UI Framework | Avalonia UI | 11.2.3 |
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.4.2 |
| DI Container | Microsoft.Extensions.DependencyInjection | 10.0.8 |
| Logging | Microsoft.Extensions.Logging | 10.0.8 |
| Hosting | Microsoft.Extensions.Hosting | 10.0.8 |
| Config | Microsoft.Extensions.Configuration (JSON) | 10.0.8 |
| Audio | NAudio + AudioSwitcher.AudioApi.CoreAudio | 2.3.0 / 3.0.3 |
| Hardware Monitor | LibreHardwareMonitorLib | 0.9.6 |
| Serialization | Newtonsoft.Json | 13.0.4 |
| WMI | System.Management | 10.0.8 |
| WinForms interop | `UseWindowsForms` (for NotifyIcon) | implicit |
| Icons | Segoe Fluent Icons (system font) | Win11 |
| Notifications | Custom Avalonia toast stack | n/a |
| Installer | WiX Toolset | 6.x |

## Project Structure

```
CenterHubNew/
├── App.axaml / App.axaml.cs         # Application entry, DI host, DataTemplates
├── MainWindow.axaml / .axaml.cs     # Main window with sidebar navigation
├── MVVM/
│   ├── Models/                      # Plain data models (no UI deps)
│   ├── Services/                    # Singletons: SystemMonitor, Audio, Clipboard…
│   ├── ViewModel/                   # Transient ViewModels (ObservableObject)
│   ├── View/                        # Avalonia UserControls (.axaml)
│   ├── Converters/                  # IValueConverter implementations
│   ├── Configuration/               # Config binding helpers
│   └── Navigation/                  # INavigationAware interface
├── Resources/Styles/Theme.axaml     # Design tokens (colors, brushes, shared styles)
├── Theme/                           # Per-control style overrides
├── Fonts/                           # Quicksand variable font
└── Images/                          # App icon and assets
```

## Architecture Rules

### ViewModels
- Inherit `BaseViewModel` (which inherits `ObservableObject`, implements `IDisposable`)
- Use `[ObservableProperty]` for bindable properties
- Use `[RelayCommand]` for commands
- Check `IsDisposed` at the top of every command and timer tick
- Call `base.Dispose(disposing)` at the end of `Dispose(bool)`
- Access other services via constructor injection or `App.Services.GetService<T>()`

### Services
- Singletons registered in `App.axaml.cs` `ConfigureServices`
- Never reference UI controls directly
- Use `Avalonia.Threading.Dispatcher.UIThread.Post()` when updating from background threads (replaces WPF `App.Current.Dispatcher.Invoke`)

### Views
- UserControls as `.axaml` files
- DataContext set via DI + DataTemplate mapping in App.axaml
- No code-behind logic — only event passthrough to ViewModel commands
- Use `IsVisible="{Binding BoolProp}"` directly instead of BoolToVisibility converters

## Avalonia-Specific Notes

### WPF → Avalonia migration cheatsheet

| WPF | Avalonia |
|-----|----------|
| `xmlns="http://...wpf"` | `xmlns="https://github.com/avaloniaui"` |
| `DispatcherTimer` (System.Windows) | `Avalonia.Threading.DispatcherTimer` |
| `App.Current.Dispatcher.Invoke` | `Dispatcher.UIThread.Post(...)` |
| `AllowsTransparency="True"` | `TransparencyLevelHint="AcrylicBlur"` |
| `WindowStyle="None"` | `SystemDecorations="None"` |
| `DragMove()` | `BeginMoveDrag(e)` |
| `BoolToVisibilityConverter` | `IsVisible="{Binding ...}"` |
| `StringFormat='{}{0:F1}'` | `StringFormat='{0:F1}'` (no `{}` prefix) |
| `{d:DesignInstance}` | `x:DataType="viewmodel:Foo"` |
| `ControlTemplate.Triggers` | `Styles` with pseudo-class selectors |
| `:IsMouseOver` trigger | `:pointerover` pseudo-class |
| `:IsChecked` trigger | `:checked` pseudo-class |
| `:IsFocused` trigger | `:focus` pseudo-class |
| `HwndSource` | WndProc subclassing via `SetWindowLongPtr` |
| `KeyInterop.VirtualKeyFromKey` | Custom `AvaloniaKeyToVK` mapping |
| `System.Windows.Input.Key` | `Avalonia.Input.Key` |
| `ModifierKeys` | `KeyModifiers` |

### Getting the native HWND in Avalonia
```csharp
var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
```

### DataTemplates
Defined in `App.axaml` under `<Application.DataTemplates>`. Each ViewModel type maps to its View.

### Themes
- Base: `<FluentTheme/>` in App.axaml Styles
- Override: `avares://CenterHubNew/Resources/Styles/Theme.axaml`
- Design tokens live in `Theme.axaml` **ThemeDictionaries** (`Dark` and `Light`): charcoal + sky blue (#4CC2FF) / light + #0078D4. Views reference them with `{DynamicResource …}` so theme and accent switch live — never `StaticResource` for a token, never a hard-coded colour that only works on one theme (translucent white overlays → `HoverOverlayBrush` / `SelectedOverlayBrush` / `TrackBrush`).
- `ThemeService` applies `UiSettings.Theme` (Dark / Light / System) and `AccentColor` (`#RRGGBB`, `"windows"` = follow Windows, null = default). A custom accent is layered on through `Application.Resources.ThemeDictionaries` + Fluent's palette, derived per theme so it stays readable (`AccentPalette`, unit-tested).

## App shell (v7 UI)

- **Window**: native Windows frame (`ExtendClientAreaToDecorationsHint`), resizable to 480×420; size/position/maximized remembered in `ui.json`. Minimize/close-to-tray follow `UiSettings`.
- **Pages** are listed once in `MVVM/Navigation/PageRegistry.cs` (key, title, glyph, group, view-model type). The sidebar, command palette, settings page and start page all come from it. Titles are what UI tests look for — don't rename them casually.
- **ShellService** (`IShellHost` = MainViewModel): navigate, open the palette / setup wizard, zoom, sidebar — use it from pages, cards, tray and palette instead of referencing MainViewModel.
- **Overlays** implement `IOverlayViewModel` (command palette, setup wizard) and are shown by `MainViewModel.ShowOverlay`.
- **Sidebar**: Auto = full ≥ 1200 px, icon rail 760–1199, drawer below; users can hide / pin / reorder pages (Settings).
- **Page layout**: every page is a `controls:PageHost` (title + subtitle + header actions, `Mode="Reading"` max 900 wide or `Mode="Data"` full width). Reflow with `controls:AdaptiveColumnsPanel` and `controls:Responsive.NarrowBelow` (`:narrow` pseudo-class; Avalonia 11.2 has no container queries). Pages must fit at 440 px of content width. See `Resources/Styles/Controls.axaml`.
- **Zoom** (Ctrl +/−/0, Ctrl+wheel, 80–150 %) is a `LayoutTransformControl`; **Density** compact = `.compact` class on the content root.
- **Status strip** (Banana status, mic, metronome mini-transport), **command palette** (Ctrl+K / Ctrl+Shift+P, optional global hotkey), **Home dashboard** of cards (`MVVM/ViewModel/Dashboard/`, also used by the Favorites window), **Settings** page (start with Windows via HKCU Run, backup/restore of `%AppData%\CenterHub\*.json`), richer **tray menu** (presets, Direct, mic, metronome, restart Voicemeeter).
- `MicrophoneService` is the one place that mutes the communications mic (hotkey, strip, palette, tray, cards).
- First-run **audio setup wizard** (`SetupWizardViewModel`) when `UiSettings.AudioSetupCompleted` is false; re-run from Settings or the palette.

## Compact Favorites Window

`FavoritesWindow.axaml` — resizable always-on-top panel (default 350×500) for the secondary (14") screen:
- Shows dashboard cards from `UiSettings.FavoritesCards` (default: system, volume, metronome); editable in place
- Draggable, snaps to screen edges/corners; one instance (★ again brings it forward)
- Opened via the ★ button, tray menu or global hotkey

## Metronome / Drum Machine

The Metronome page is a click **and** a BeatBuddy-style drum machine, all synthesized (no sample files):
- `DrumMachineEngine` (an NAudio `ISampleProvider`) is the sample-accurate sequencer + mixer: 48 ticks per beat, song state machine (count-in → intro → part grooves → fills → transition to next part → outro + final hit), click layer (accents, subdivisions), gap trainer. It has no UI or device dependency — unit tests render it offline.
- `DrumKit` provides every drum voice and click sound: "Real drums" (default) plays recorded hits from the embedded `Assets/Drums/acoustic.chdk` (public domain, see `Assets/Drums/README.md`), with velocity layers and alternating hits; Rock / Electro / Jazz are synthesized. Deterministic.
- `DrumStyleLibrary` (+ `.RockPop.cs`, `.Groove.cs`) holds the styles, written as drum tabs — see `DrumBar.Parse` in `MVVM/Models/DrumModels.cs`. Every bar of a style must have `Beats × StepsPerBeat` steps; `DrumStyleLibrary.Validate` (unit-tested) catches typos. Fills keep the groove for the first half of the bar because they can be triggered mid-bar.
- `MetronomeService` owns the audio output (WASAPI shared/event, WaveOut fallback; opened on demand, closed ~1.5 s after going idle) and delivers the engine's position events on the UI thread *when they become audible*, so the lights match the sound.
- Settings persist in `%AppData%\CenterHub\metronome.json` via `MetronomeSettingsService`.
- Hotkeys (unbound by default): Start/Stop, Drum Fill, Next Song Part, Tap Tempo — a USB footswitch that sends keys works as a pedal.

## Key Conventions

- Version bumped in `CenterHubNew.csproj` → `<Version>`
- User data and settings live in `%AppData%\CenterHub\*.json` — never next to the exe (Program Files is read-only for users and wiped by MSI upgrades). Write with `AtomicFile.WriteAllText`; on a JSON parse failure call `AtomicFile.QuarantineCorrupt` before falling back to defaults
- Toast notifications via the singleton `ToastService.Instance` (shown bottom-right)
- GlobalHotkeyService requires a window HWND — initialized after MainWindow loads. Map keys with `GlobalHotkeyService.KeyToVirtualKey` (Avalonia `Key` is not a Win32 VK code)
- View-models that global hotkeys act on (Sound, Standing, Clipboard, AutoClicker, Soundboard, Metronome) are **singletons**
- Single-instance enforced in `Program.Main` via `Mutex` ("CenterHubNew_SingleInstance_Mutex"); a second launch signals the first to restore its window
- Minimize hides to the tray (`Hide()`); closing the main window quits (`ShutdownMode.OnMainWindowClose`), no confirmation
- Build/installer via `build-installer.ps1`

## Testing

- Unit tests: `dotnet test tests/CenterHubNew.Tests/CenterHubNew.Tests.csproj` (xUnit; fakes in `Fakes.cs`; never touch the real `%AppData%`)
- UI regression: `powershell -ExecutionPolicy Bypass -File tools/regression.ps1` — drives the real app via UI Automation and asserts behaviour (navigation, Notes autosave, tray, single instance, close)
- Page screenshots: `tools/ui-smoke.ps1`
- Headless (no window, no sound — safe while the PC is in use): `dotnet run --project tools/page-render -- <outDir>` renders the Metronome page to PNGs; add `audio` to check beat timing on the real audio device at volume 0, or `com-probe` to check audio-device enumeration
- Real Voicemeeter start/restart/crash/hang recovery: `dotnet run --project tools/page-render -- <outDir> voicemeeter` — **restarts Banana repeatedly (audio drops)**; only when asked
- Builds must stay at 0 warnings. See `.claude/skills/centerhub-dev/SKILL.md` for gotchas and the release checklist.
