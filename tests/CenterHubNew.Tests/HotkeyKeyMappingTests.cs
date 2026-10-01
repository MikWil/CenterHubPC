using Avalonia.Input;
using CenterHubNew.MVVM.Services;
using Xunit;

namespace CenterHubNew.Tests;

/// <summary>
/// Regression: hotkeys used to cast Avalonia's Key enum straight to a Win32 VK code.
/// Avalonia follows the WPF layout (Key.K = 54 = VK '6'), so every hotkey hit the wrong key.
/// </summary>
public class HotkeyKeyMappingTests
{
    [Theory]
    [InlineData(Key.A, 0x41)]
    [InlineData(Key.K, 0x4B)]
    [InlineData(Key.Z, 0x5A)]
    [InlineData(Key.D0, 0x30)]
    [InlineData(Key.D9, 0x39)]
    [InlineData(Key.F1, 0x70)]
    [InlineData(Key.F12, 0x7B)]
    [InlineData(Key.F24, 0x87)]
    [InlineData(Key.NumPad0, 0x60)]
    [InlineData(Key.NumPad9, 0x69)]
    [InlineData(Key.Space, 0x20)]
    [InlineData(Key.Escape, 0x1B)]
    [InlineData(Key.Home, 0x24)]
    [InlineData(Key.OemPlus, 0xBB)]
    [InlineData(Key.MediaPlayPause, 0xB3)]
    public void Maps_avalonia_key_to_win32_virtual_key(Key key, int expectedVk)
        => Assert.Equal((uint)expectedVk, GlobalHotkeyService.KeyToVirtualKey(key));

    [Fact]
    public void Unmappable_key_returns_zero_so_it_is_not_registered()
        => Assert.Equal(0u, GlobalHotkeyService.KeyToVirtualKey(Key.None));

    [Fact]
    public void Avalonia_key_values_are_not_win32_codes()
        // Guard against anyone "simplifying" back to (uint)binding.Key.
        => Assert.NotEqual((uint)Key.K, GlobalHotkeyService.KeyToVirtualKey(Key.K));
}
