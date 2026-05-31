# CenterHub

A modern Windows 11 productivity hub — system monitoring, audio profiles, soundboard, clipboard history, standing-timer, auto-clicker, hotkeys, and developer tools — wrapped in a Fluent / Mica UI.

Built with **Avalonia UI 11** on **.NET 10 LTS**.

<img width="1864" height="941" alt="dashboard" src="https://github.com/user-attachments/assets/2d11f060-e50d-4273-a421-f6c67e2f1ef1" />
<img width="1361" height="870" alt="monitoring" src="https://github.com/user-attachments/assets/dd9a2651-6436-4fc1-8c0f-6e1e8517bc84" />
<img width="1369" height="878" alt="sound" src="https://github.com/user-attachments/assets/9f493e09-fcd7-461d-a27e-e89ca7d8ee6d" />
<img width="1361" height="877" alt="standing-timer" src="https://github.com/user-attachments/assets/ca271814-582f-4549-94e3-34c1be5ef8dd" />
<img width="1359" height="880" alt="utilities" src="https://github.com/user-attachments/assets/f0cf4724-3f9b-44f9-a8c5-13f89dac080a" />
<img width="1359" height="878" alt="hotkeys" src="https://github.com/user-attachments/assets/aab0555a-0e52-4002-babe-21e4792c0f64" />
<img width="1371" height="872" alt="auto-clicker" src="https://github.com/user-attachments/assets/eb791011-6126-4268-bd9e-df5d5cb93ea9" />

---

## ✨ Features

### Monitoring
- Real-time **CPU / GPU / RAM** percentages with temperature badges (now + max)
- **Storage tiles** for every fixed drive with used / total and a fill bar
- Color-coded per metric (blue / violet / emerald / amber)

### Sound
- Three switchable **audio profiles** (device + volume per profile)
- Quick-controls strip — output device, master volume, mic mute toggle
- **Advanced sound controls** window — per-app volume mixer
- **Compact Favorites panel** (always-on-top, 380×640) for secondary monitors

### Soundboard
- Drag-drop or browse audio clips into a tile grid
- Per-clip volume and remove control
- Output to any device, optional monitor mix (hear yourself)
- Discord setup wizard built in

### Productivity
- **Standing Timer** — live MM:SS countdown, phase progress bar, "next switch at HH:MM", session stats, 4 presets (Pomodoro / Office / Balanced / Endurance), skip-phase command
- **Quick Notes** — list view + editor pane, export to text file
- **Auto Clicker** — Silent / Teleport / Follow modes, arm countdown, failsafe corner-abort, click limit, ±jitter, Left/Right/Middle button picker
- **Clipboard history** — toggleable capture, pin entries, copy back, delete

### Developer tools (Utilities)
- JSON stringify / unstringify
- Base64 encode / decode
- URL encode / decode
- Hash generator (MD5 / SHA256 / SHA512)
- Unix timestamp ↔ date
- GUID generator (standard / uppercase / no-dashes)

### Global hotkeys
- Bind any system-wide shortcut to: show/hide window, mic mute, audio profile switch, soundboard play, standing timer start/stop, clipboard monitor toggle, auto-clicker start/stop, auto-clicker capture position

---

## 🪟 Design language — Fluent / Mica

- **Mica backdrop** — desktop wallpaper subtly tints the chrome on Windows 11
- **Acrylic fallback** for Windows 10 1903+
- **Segoe Fluent Icons** in the sidebar (Speed gauge for Monitoring, Stopwatch for Standing, Volume for Sound, etc.)
- **Segoe UI Variable Display** for large metric numbers, **Segoe UI Variable Text** for body
- Hairline borders, 12 px card corners, 6 px button corners, 120 ms BrushTransition hover

---

## 📦 Install

### MSI installer (recommended)
Download **`CenterHub.msi`** from the [latest release](https://github.com/MikWil/WilkonCenterHub/releases/latest) and run it. Code-signed, installs to `%LOCALAPPDATA%\CenterHub`, creates Start-Menu + Desktop shortcuts, no admin needed. Upgrading from an earlier version is automatic — MSI MajorUpgrade removes the old install first.

### Portable
Grab `CenterHub-vX.Y.Z-Portable.zip`, extract anywhere, run `CenterHubNew.exe`. Settings live in `appsettings.json` next to the binary.

### Requirements
- **Windows 11** (recommended) — Mica backdrop is native
- **Windows 10 1903+** — falls back to Acrylic
- **.NET 10 Desktop Runtime** — Windows will offer to install it on first run if missing

---

## 🛠 Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Runtime | .NET (LTS) | **10.0** |
| Target framework | `net10.0-windows10.0.22621.0` | Win11 22H2+ |
| UI Framework | Avalonia UI | 11.2.3 |
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.4.2 |
| Dependency Injection | Microsoft.Extensions.DependencyInjection | 10.0.8 |
| Hosting | Microsoft.Extensions.Hosting | 10.0.8 |
| Logging | Microsoft.Extensions.Logging | 10.0.8 |
| Configuration (JSON) | Microsoft.Extensions.Configuration | 10.0.8 |
| Audio device API | NAudio + AudioSwitcher.AudioApi.CoreAudio | 2.3.0 / 3.0.3 |
| Hardware Monitor | LibreHardwareMonitorLib | 0.9.6 |
| JSON serialization | Newtonsoft.Json | 13.0.4 |
| WMI | System.Management | 10.0.8 |
| WinForms interop | `UseWindowsForms` (NotifyIcon, dialogs) | implicit |
| Icons | Segoe Fluent Icons (system font) | Win11 |
| Installer | WiX Toolset | 6.x |

---

## 🏗 Architecture

### MVVM with DI

```
CenterHubNew/
├── App.axaml / App.axaml.cs         # Application entry, generic host, DI
├── MainWindow.axaml / .axaml.cs     # Shell with sidebar navigation
├── MVVM/
│   ├── Models/                      # POCOs (no UI deps)
│   ├── Services/                    # Singletons (SystemMonitor, Audio, Clipboard, etc.)
│   ├── ViewModel/                   # Transient ObservableObject view-models
│   ├── View/                        # Avalonia UserControls (.axaml)
│   ├── Converters/                  # IValueConverter implementations
│   ├── Configuration/               # Strongly-typed config binding
│   └── Navigation/                  # INavigationAware
├── Resources/Styles/Theme.axaml     # Fluent / Mica design tokens + styles
├── installer/                       # WiX 6 MSI project
└── build-installer.ps1              # Build pipeline (publish + sign + WiX)
```

### Conventions

- **ViewModels** inherit `BaseViewModel` (`ObservableObject` + `IDisposable`), use `[ObservableProperty]` and `[RelayCommand]`, and check `IsDisposed` at the top of every command and timer tick.
- **Services** are singletons registered in `App.axaml.cs`; never reference UI controls directly.
- **Cross-thread UI updates** use `Avalonia.Threading.Dispatcher.UIThread.Post()` (not WPF's `App.Current.Dispatcher.Invoke`).
- **Views** are dumb — only XAML + event passthroughs.
- **No emojis in XAML** — Segoe Fluent Icons for nav, plain Unicode geometric chars (`×`, `▶`, `↻`) for inline actions, plain text everywhere else.

See [`CLAUDE.md`](CLAUDE.md) for the full developer guide.

---

## 🔧 Development

### Prerequisites
- Windows 11
- **.NET 10 SDK** (10.0.300+) — `winget install Microsoft.DotNet.SDK.10`
- **WiX Toolset 6** — `dotnet tool install --global wix`
- Visual Studio 2022 17.12+ or VS Code with the C# Dev Kit

### Build
```powershell
dotnet restore
dotnet build -c Release
```

### Run
```powershell
dotnet run -c Release
```

### Build a signed MSI
```powershell
.\build-installer.ps1
```
Output: `installer\bin\x64\Release\CenterHub.msi` plus two ZIPs (MSI-wrapped and portable). The script also signs both artifacts with the configured cert thumbprint.

---

## ⚙ Configuration

Settings live in `appsettings.json` next to the executable:

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information" }
  },
  "Application": {
    "StartMinimized": false,
    "ShowInSystemTray": true
  },
  "SystemMonitor": {
    "UpdateInterval": 2000,
    "EnableCpuMonitoring": true,
    "EnableGpuMonitoring": true
  }
}
```

Sound profiles, hotkey bindings, soundboard entries, and clipboard pins are persisted to per-user JSON files alongside `appsettings.json`.

---

## 🚀 Releases

See the [Releases page](https://github.com/MikWil/WilkonCenterHub/releases) for changelogs and downloadable installers.

Latest highlights:
- **v5.1.0** — .NET 10 LTS runtime, Microsoft.Extensions 10.0.8, package refresh
- **v5.0.0** — Full Fluent / Mica revamp, AutoClicker silent-click rewrite, Standing Timer hero panel
- **v4.x** — Migrated from WPF to Avalonia UI 11

---

## 🤝 Contributing

1. Fork & branch off `master`
2. Make your changes; the build must stay clean (0 errors)
3. Follow the conventions in `CLAUDE.md` (BaseViewModel, DI, no UI refs in services)
4. Open a pull request

---

## 📄 License

MIT. See [`LICENSE`](LICENSE) for details.
