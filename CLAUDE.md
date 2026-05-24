# CenterHub — Developer Guide

## Project Overview

CenterHub is a Windows desktop productivity app (system monitoring, audio controls, file management, productivity tools). Built with **Avalonia UI 11** + **.NET 8** following the MVVM pattern.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | Avalonia UI 11.x (migrated from WPF) |
| MVVM Toolkit | CommunityToolkit.Mvvm 8.x |
| DI Container | Microsoft.Extensions.DependencyInjection |
| Logging | Microsoft.Extensions.Logging |
| Audio | NAudio, AudioSwitcher.AudioApi.CoreAudio |
| Hardware Monitor | LibreHardwareMonitorLib |
| Config | Microsoft.Extensions.Configuration (JSON) |
| Notifications | Custom Avalonia toast stack |
| Serialization | Newtonsoft.Json |

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
- Design tokens: dark charcoal palette with sky-blue accent (#7DD3FC)

## Compact Favorites Window

`FavoritesWindow.axaml` — 350×500px always-on-top panel for the secondary (14") screen:
- Live CPU / GPU / RAM stats (2s refresh via SystemMonitorService)
- Volume slider + mic mute toggle (via SoundViewModel)
- Draggable, snaps to screen corners
- Opened via File menu or global hotkey (Alt+F9 default)

## Key Conventions

- Version bumped in `CenterHubNew.csproj` → `<Version>`
- `appsettings.json` is the only user-facing config; never hard-code paths
- Toast notifications via the singleton `ToastService.Instance`
- GlobalHotkeyService requires a window HWND — initialized after MainWindow loads
- Single-instance enforced via `Mutex` ("CenterHubNew_SingleInstance_Mutex")
- Build/installer via `build-installer.ps1`
