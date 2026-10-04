using System;
using CenterHubNew.MVVM.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>One sidebar entry. The RadioButton binds <see cref="IsSelected"/> TwoWay.</summary>
    public sealed partial class NavItemViewModel : ObservableObject
    {
        public NavItemViewModel(PageDescriptor page)
        {
            Key = page.Key;
            Title = page.Title;
            Glyph = page.Glyph;
            Description = page.Tooltip;
            _tooltip = page.Tooltip;
        }

        public string Key { get; }
        public string Title { get; }
        public string Glyph { get; }

        /// <summary>The page's own description (the rail swaps <see cref="Tooltip"/> to the title).</summary>
        public string Description { get; }

        [ObservableProperty] private string _tooltip;

        [ObservableProperty] private bool _isSelected;

        /// <summary>Raised when <see cref="IsSelected"/> becomes true (mouse click or UI automation alike).</summary>
        public event Action<NavItemViewModel>? Selected;

        // Keyboard/UIA only flip IsChecked (no Command runs), so navigation hangs off this flag.
        partial void OnIsSelectedChanged(bool value)
        {
            if (value) Selected?.Invoke(this);
        }
    }
}
