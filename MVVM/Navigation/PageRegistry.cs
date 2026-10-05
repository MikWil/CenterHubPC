using System;
using System.Collections.Generic;
using System.Linq;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.Navigation
{
    /// <summary>One page of the app: how it appears in the sidebar and command palette, and which view-model shows it.</summary>
    /// <param name="Key">Stable id, persisted in ui.json (order, hidden, pinned, start page). Never rename.</param>
    /// <param name="Title">Sidebar label. UI automation finds sidebar items by this name — keep it equal to the old labels.</param>
    /// <param name="Glyph">Segoe Fluent Icons character.</param>
    /// <param name="Group">Sidebar section header.</param>
    /// <param name="Keywords">Extra words the command palette matches.</param>
    public sealed record PageDescriptor(
        string Key,
        string Title,
        string Glyph,
        string Group,
        string Tooltip,
        Type ViewModelType,
        string Keywords = "");

    /// <summary>Every page, in default order. The sidebar, the command palette and the start-page setting read this.</summary>
    public static class PageRegistry
    {
        public static IReadOnlyList<string> Groups { get; } = new[] { "Dashboard", "Productivity", "Media", "Tools", "Hobby", "System" };

        public static IReadOnlyList<PageDescriptor> All { get; } = new[]
        {
            new PageDescriptor("home",        "Home",         "", "Dashboard",    "Your dashboard",                          typeof(DashboardViewModel),     "start overview widgets cards"),
            new PageDescriptor("monitoring",  "Monitoring",   "", "Dashboard",    "Real-time hardware monitoring",           typeof(MonitoringViewModel),    "cpu gpu ram temperature storage"),
            new PageDescriptor("standing",    "Standing",     "", "Productivity", "Sit/Stand timer",                         typeof(StandingViewModel),      "timer sit stand break"),
            new PageDescriptor("notes",       "Notes",        "", "Productivity", "Quick notes",                             typeof(QuickNotesViewModel),    "text write"),
            new PageDescriptor("layouts",     "Layouts",      "", "Productivity", "Save and restore window arrangements",    typeof(WindowLayoutsViewModel), "windows arrange snap"),
            new PageDescriptor("sound",       "Sound",        "", "Media",        "Audio routing, presets and devices",      typeof(SoundViewModel),         "audio voicemeeter banana preset discord headset mic routing"),
            new PageDescriptor("soundboard",  "Soundboard",   "", "Media",        "Trigger sound clips",                     typeof(SoundboardViewModel),    "clips effects"),
            new PageDescriptor("utilities",   "Utilities",    "", "Tools",        "Encoders, converters, generators",        typeof(UtilitiesViewModel),     "json base64 convert"),
            new PageDescriptor("autoclicker", "Auto Clicker", "", "Tools",        "Automated mouse clicks",                  typeof(AutoClickerViewModel),   "mouse click"),
            new PageDescriptor("clipboard",   "Clipboard",    "", "Tools",        "Clipboard history",                       typeof(ClipboardViewModel),     "copy paste history"),
            new PageDescriptor("randomizer",  "Randomizer",   "", "Hobby",        "Pick a random option from a custom list", typeof(RandomizerViewModel),    "random pick dice"),
            new PageDescriptor("metronome",   "Jam Station", "", "Hobby",        "Drum machine, metronome and guitar looper", typeof(MetronomeViewModel),   "metronome drums beat tempo bpm practice guitar setlist looper loop click"),
            new PageDescriptor("network",     "Network",      "", "System",       "Wi-Fi signal + connection fixes",         typeof(NetworkViewModel),       "wifi internet ping"),
            new PageDescriptor("hotkeys",     "Hotkeys",      "", "System",       "Global keyboard shortcuts",               typeof(HotkeySettingsViewModel),"shortcuts keys keyboard"),
            new PageDescriptor("settings",    "Settings",     "", "System",       "App settings, theme and backup",          typeof(SettingsViewModel),      "preferences theme zoom tray startup backup"),
        };

        public static PageDescriptor? Find(string? key) =>
            string.IsNullOrEmpty(key) ? null : All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}
