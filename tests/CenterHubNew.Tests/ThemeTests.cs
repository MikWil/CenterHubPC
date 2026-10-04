using Avalonia.Media;
using CenterHubNew.MVVM.Services;
using Xunit;
using Color = Avalonia.Media.Color;
using Colors = Avalonia.Media.Colors;

namespace CenterHubNew.Tests;

public class ThemeTests
{
    [Fact]
    public void No_accent_setting_means_the_built_in_palette()
    {
        Assert.Null(ThemeService.ResolveAccent(null, Colors.Red));
        Assert.Null(ThemeService.ResolveAccent("  ", Colors.Red));
        Assert.Null(ThemeService.ResolveAccent("not a colour", Colors.Red));
    }

    [Fact]
    public void Windows_accent_follows_the_platform_colour()
    {
        Assert.Equal(Colors.Red, ThemeService.ResolveAccent("windows", Colors.Red));
        Assert.Equal(Colors.Red, ThemeService.ResolveAccent("Windows", Colors.Red));
        Assert.Null(ThemeService.ResolveAccent("windows", null));
    }

    [Fact]
    public void Hex_accent_is_parsed_opaque()
    {
        var c = ThemeService.ResolveAccent("#FF8800", null);
        Assert.Equal(Color.FromRgb(0xFF, 0x88, 0x00), c);
    }

    [Theory]
    [InlineData("#4CC2FF")]
    [InlineData("#0B1A60")]   // very dark navy
    [InlineData("#FFE066")]   // pale yellow
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void Accent_stays_readable_on_both_themes(string hex)
    {
        var c = Color.Parse(hex);
        var dark = AccentPalette.ForDark(c);
        var light = AccentPalette.ForLight(c);

        // Bright enough on the dark charcoal surface, dark enough on the light one.
        Assert.True(AccentPalette.Luminance(dark.Primary) >= 0.28, $"dark primary {dark.Primary} too dark");
        Assert.True(AccentPalette.Luminance(light.Primary) <= 0.24, $"light primary {light.Primary} too light");

        // Text on the accent contrasts with it (WCAG ≥ 3:1 for large/bold button text).
        Assert.True(Contrast(dark.Primary, dark.OnAccent) >= 3, $"text on {dark.Primary}");
        Assert.True(Contrast(light.Primary, light.OnAccent) >= 3, $"text on {light.Primary}");
    }

    [Fact]
    public void Settings_keep_the_windows_accent_and_drop_junk()
    {
        var s = new CenterHubNew.MVVM.Models.UiSettings { AccentColor = "WINDOWS" };
        UiSettingsService.Sanitize(s);
        Assert.Equal("windows", s.AccentColor);

        s.AccentColor = "red";
        UiSettingsService.Sanitize(s);
        Assert.Null(s.AccentColor);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = AccentPalette.Luminance(a), lb = AccentPalette.Luminance(b);
        return (System.Math.Max(la, lb) + 0.05) / (System.Math.Min(la, lb) + 0.05);
    }
}
