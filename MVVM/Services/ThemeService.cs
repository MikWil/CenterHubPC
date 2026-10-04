using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CenterHubNew.MVVM.Models;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Applies <see cref="UiSettings.Theme"/> (dark / light / follow Windows) and
    /// <see cref="UiSettings.AccentColor"/> to the running app. The palettes live in
    /// Theme.axaml's ThemeDictionaries; a custom accent is layered on top through
    /// Application.Resources (looked up before the styles) and Fluent's palette, so pages that use
    /// <c>{DynamicResource …}</c> repaint live. DI singleton; call <see cref="Apply"/> once on the
    /// UI thread before the first window is created.
    /// </summary>
    public sealed class ThemeService : IDisposable
    {
        /// <summary><see cref="UiSettings.AccentColor"/> value meaning "use the Windows accent colour".</summary>
        public const string WindowsAccent = "windows";

        public static readonly Color DefaultDarkAccent = Color.Parse("#4CC2FF");
        public static readonly Color DefaultLightAccent = Color.Parse("#0078D4");

        private readonly UiSettingsService _ui;
        private readonly ILogger<ThemeService>? _logger;
        private IPlatformSettings? _platform;
        private AppTheme _appliedTheme = (AppTheme)(-1);
        private string? _appliedAccent = "\0";

        public ThemeService(UiSettingsService ui, ILogger<ThemeService>? logger = null)
        {
            _ui = ui;
            _logger = logger;
            _ui.Changed += OnSettingsChanged;
        }

        /// <summary>Apply the saved theme and accent now (UI thread).</summary>
        public void Apply()
        {
            var app = Application.Current;
            if (app is null) return;

            if (_platform is null)
            {
                _platform = app.PlatformSettings;
                if (_platform != null) _platform.ColorValuesChanged += OnPlatformColorsChanged;
            }

            var s = _ui.Current;
            try
            {
                if (s.Theme != _appliedTheme)
                {
                    app.RequestedThemeVariant = s.Theme switch
                    {
                        AppTheme.Light => ThemeVariant.Light,
                        AppTheme.System => ThemeVariant.Default,
                        _ => ThemeVariant.Dark,
                    };
                    _appliedTheme = s.Theme;
                }

                ApplyAccent(app, s.AccentColor);
                _appliedAccent = s.AccentColor;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not apply the theme");
            }
        }

        private void ApplyAccent(Application app, string? setting)
        {
            Color? custom = ResolveAccent(setting, _platform?.GetColorValues().AccentColor1);

            var dict = app.Resources.ThemeDictionaries;
            if (custom is null)
            {
                dict.Remove(ThemeVariant.Dark);
                dict.Remove(ThemeVariant.Light);
            }
            else
            {
                dict[ThemeVariant.Dark] = BuildAccentDictionary(AccentPalette.ForDark(custom.Value));
                dict[ThemeVariant.Light] = BuildAccentDictionary(AccentPalette.ForLight(custom.Value));
            }

            var fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
            if (fluent != null)
            {
                SetFluentAccent(fluent, ThemeVariant.Dark, custom is null ? DefaultDarkAccent : AccentPalette.ForDark(custom.Value).Primary);
                SetFluentAccent(fluent, ThemeVariant.Light, custom is null ? DefaultLightAccent : AccentPalette.ForLight(custom.Value).Primary);
            }
        }

        private static void SetFluentAccent(FluentTheme fluent, ThemeVariant variant, Color accent)
        {
            if (fluent.Palettes.TryGetValue(variant, out var palette)) palette.Accent = accent;
            else fluent.Palettes[variant] = new ColorPaletteResources { Accent = accent };
        }

        private static ResourceDictionary BuildAccentDictionary(AccentPalette p) => new()
        {
            ["AccentPrimary"] = p.Primary,
            ["AccentHover"] = p.Hover,
            ["AccentPressed"] = p.Pressed,
            ["AccentLight"] = p.Light,
            ["AccentSubtle"] = p.Subtle,
            ["TextOnAccent"] = p.OnAccent,
            ["AccentBrush"] = new SolidColorBrush(p.Primary),
            ["AccentHoverBrush"] = new SolidColorBrush(p.Hover),
            ["AccentPressedBrush"] = new SolidColorBrush(p.Pressed),
            ["AccentLightBrush"] = new SolidColorBrush(p.Light),
            ["AccentSubtleBrush"] = new SolidColorBrush(p.Subtle),
            ["TextOnAccentBrush"] = new SolidColorBrush(p.OnAccent),
        };

        /// <summary>The custom accent for a setting value, or null for CenterHub's own palette.</summary>
        internal static Color? ResolveAccent(string? setting, Color? windowsAccent)
        {
            if (string.IsNullOrWhiteSpace(setting)) return null;
            if (string.Equals(setting, WindowsAccent, StringComparison.OrdinalIgnoreCase)) return windowsAccent;
            return Color.TryParse(setting, out var c) ? Color.FromRgb(c.R, c.G, c.B) : null;
        }

        private void OnSettingsChanged(UiSettings s)
        {
            if (s.Theme == _appliedTheme && s.AccentColor == _appliedAccent) return;
            Dispatcher.UIThread.Post(Apply);
        }

        private void OnPlatformColorsChanged(object? sender, PlatformColorValues e)
        {
            // Windows' accent changed — only matters when we follow it.
            if (string.Equals(_ui.Current.AccentColor, WindowsAccent, StringComparison.OrdinalIgnoreCase))
                Dispatcher.UIThread.Post(() => { if (Application.Current is { } app) ApplyAccent(app, _ui.Current.AccentColor); });
        }

        public void Dispose()
        {
            _ui.Changed -= OnSettingsChanged;
            if (_platform != null) _platform.ColorValuesChanged -= OnPlatformColorsChanged;
        }
    }

    /// <summary>The accent shades derived from one colour, tuned for a dark or a light surface.</summary>
    internal readonly record struct AccentPalette(Color Primary, Color Hover, Color Pressed, Color Light, Color Subtle, Color OnAccent)
    {
        private static readonly Color White = Colors.White;
        private static readonly Color Black = Colors.Black;
        private static readonly Color DarkText = Color.Parse("#0B1320");

        /// <summary>On dark surfaces the accent must be bright enough to read: lift dark picks.</summary>
        public static AccentPalette ForDark(Color c)
        {
            var primary = c;
            for (int i = 0; i < 10 && Luminance(primary) < 0.30; i++) primary = Mix(primary, White, 0.15);
            return new AccentPalette(
                primary,
                Mix(primary, White, 0.25),
                Mix(primary, Black, 0.12),
                Mix(primary, White, 0.65),
                Color.FromArgb(0x30, primary.R, primary.G, primary.B),
                OnAccentFor(primary));
        }

        /// <summary>On light surfaces the accent must be dark enough to read: deepen light picks.</summary>
        public static AccentPalette ForLight(Color c)
        {
            var primary = c;
            for (int i = 0; i < 10 && Luminance(primary) > 0.22; i++) primary = Mix(primary, Black, 0.15);
            return new AccentPalette(
                primary,
                Mix(primary, White, 0.12),
                Mix(primary, Black, 0.15),
                Mix(primary, Black, 0.30),
                Color.FromArgb(0x22, primary.R, primary.G, primary.B),
                OnAccentFor(primary));
        }

        /// <summary>Dark or white text, whichever contrasts more with the accent.</summary>
        public static Color OnAccentFor(Color accent)
        {
            double l = Luminance(accent);
            double withDark = (l + 0.05) / (Luminance(DarkText) + 0.05);
            double withWhite = 1.05 / (l + 0.05);
            return withDark >= withWhite ? DarkText : White;
        }

        public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

        /// <summary>WCAG relative luminance, 0 (black) – 1 (white).</summary>
        public static double Luminance(Color c)
        {
            static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }
    }
}
