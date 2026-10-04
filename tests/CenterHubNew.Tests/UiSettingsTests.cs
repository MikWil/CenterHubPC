using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.Services;
using Xunit;

namespace CenterHubNew.Tests;

public class UiSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CenterHubUi-" + Guid.NewGuid().ToString("N"));

    public UiSettingsTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }

    [Fact]
    public void Defaults_keep_todays_behaviour_and_start_on_home()
    {
        var s = new UiSettingsService(null, _folder).Current;

        Assert.Equal("home", s.StartPage);
        Assert.Equal(1.0, s.Zoom);
        Assert.True(s.MinimizeToTray);    // minimize has always hidden to the tray
        Assert.False(s.CloseToTray);      // and close has always quit
        Assert.True(s.ShowStatusStrip);
        Assert.Equal(SidebarMode.Auto, s.Sidebar);
        Assert.Equal(AppTheme.Dark, s.Theme);
    }

    [Fact]
    public void Changes_are_saved_and_announced()
    {
        var service = new UiSettingsService(null, _folder);
        UiSettings? announced = null;
        service.Changed += s => announced = s;

        service.Update(s => { s.Zoom = 1.2; s.PinnedPages.Add("metronome"); s.Theme = AppTheme.Light; s.Window.Width = 1100; });

        Assert.Same(service.Current, announced);
        var reloaded = new UiSettingsService(null, _folder).Current;
        Assert.Equal(1.2, reloaded.Zoom, 3);
        Assert.Equal(new[] { "metronome" }, reloaded.PinnedPages);
        Assert.Equal(AppTheme.Light, reloaded.Theme);
        Assert.Equal(1100, reloaded.Window.Width);
    }

    [Fact]
    public void Out_of_range_values_are_repaired()
    {
        File.WriteAllText(Path.Combine(_folder, "ui.json"),
            """{ "Zoom": 9, "StartPage": "", "AccentColor": "blue", "Window": { "Width": 50, "Height": 99999 }, "PinnedPages": ["a", "a", ""], "Sidebar": "Sideways" }""");

        var s = new UiSettingsService(null, _folder).Current;

        Assert.Equal(1.5, s.Zoom);
        Assert.Equal("home", s.StartPage);
        Assert.Null(s.AccentColor);
        Assert.Null(s.Window.Width);
        Assert.Null(s.Window.Height);
        Assert.Equal(new[] { "a" }, s.PinnedPages);
    }

    [Fact]
    public void Corrupt_file_is_quarantined_not_overwritten()
    {
        var path = Path.Combine(_folder, "ui.json");
        File.WriteAllText(path, "{ not json");

        var s = new UiSettingsService(null, _folder).Current;

        Assert.Equal("home", s.StartPage);
        Assert.Single(Directory.GetFiles(_folder, "ui.json.corrupt-*"));
    }

    [Fact]
    public void Page_registry_keys_are_unique_and_titles_match_the_sidebar_labels_ui_tests_use()
    {
        var keys = PageRegistry.All.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(PageRegistry.All, p => Assert.Contains(p.Group, PageRegistry.Groups));

        // tools/regression.ps1 and tools/smoke-quiet.ps1 find sidebar items by these names.
        foreach (var title in new[] { "Monitoring", "Standing", "Notes", "Layouts", "Sound", "Soundboard", "Utilities",
                                      "Auto Clicker", "Clipboard", "Randomizer", "Metronome", "Network", "Hotkeys" })
            Assert.Contains(PageRegistry.All, p => p.Title == title);

        Assert.NotNull(PageRegistry.Find("HOME"));
        Assert.Null(PageRegistry.Find("nope"));
    }
}
