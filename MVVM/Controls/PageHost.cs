using Avalonia;
using Avalonia.Controls;

namespace CenterHubNew.MVVM.Controls
{
    /// <summary>
    /// The standard page frame: a header (title, subtitle, actions, badge) that stays at the top while
    /// the body scrolls underneath it. The control template lives in <c>Resources/Styles/Controls.axaml</c>.
    /// </summary>
    /// <remarks>
    /// <para>The page content is the <see cref="ContentControl.Content"/> of the host.</para>
    /// <para>Pseudo-classes set on the host (usable in styles via <c>controls|PageHost:narrow /template/ …</c>):
    /// <c>:narrow</c> (host narrower than <see cref="NarrowWidth"/>; the header actions move under the title),
    /// <c>:scrollable</c> (<see cref="IsScrollable"/>), <c>:reading</c> / <c>:data</c> (<see cref="Mode"/>).</para>
    /// <para>Density: give any ancestor (the shell does this) the class <c>compact</c> and the paddings shrink;
    /// the rules are <c>.compact controls|PageHost /template/ Border#PART_Header</c> and <c>… ScrollViewer#PART_Body</c>
    /// in Controls.axaml.</para>
    /// </remarks>
    public class PageHost : ContentControl
    {
        /// <summary>Host width in DIPs below which the header actions wrap under the title.</summary>
        public const double NarrowWidth = 640;

        /// <summary>Defines the <see cref="Title"/> property.</summary>
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<PageHost, string?>(nameof(Title));

        /// <summary>Defines the <see cref="Subtitle"/> property.</summary>
        public static readonly StyledProperty<string?> SubtitleProperty =
            AvaloniaProperty.Register<PageHost, string?>(nameof(Subtitle));

        /// <summary>Defines the <see cref="HeaderActions"/> property.</summary>
        public static readonly StyledProperty<object?> HeaderActionsProperty =
            AvaloniaProperty.Register<PageHost, object?>(nameof(HeaderActions));

        /// <summary>Defines the <see cref="HeaderBadge"/> property.</summary>
        public static readonly StyledProperty<object?> HeaderBadgeProperty =
            AvaloniaProperty.Register<PageHost, object?>(nameof(HeaderBadge));

        /// <summary>Defines the <see cref="Mode"/> property.</summary>
        public static readonly StyledProperty<PageMode> ModeProperty =
            AvaloniaProperty.Register<PageHost, PageMode>(nameof(Mode), PageMode.Reading);

        /// <summary>Defines the <see cref="ContentMaxWidth"/> property.</summary>
        public static readonly StyledProperty<double> ContentMaxWidthProperty =
            AvaloniaProperty.Register<PageHost, double>(nameof(ContentMaxWidth), 900d);

        /// <summary>Defines the <see cref="IsScrollable"/> property.</summary>
        public static readonly StyledProperty<bool> IsScrollableProperty =
            AvaloniaProperty.Register<PageHost, bool>(nameof(IsScrollable), true);

        /// <summary>Defines the <see cref="EffectiveContentMaxWidth"/> property.</summary>
        public static readonly DirectProperty<PageHost, double> EffectiveContentMaxWidthProperty =
            AvaloniaProperty.RegisterDirect<PageHost, double>(
                nameof(EffectiveContentMaxWidth), o => o.EffectiveContentMaxWidth);

        private double _effectiveContentMaxWidth = 900d;

        /// <summary>Creates a page host (Reading mode, scrollable).</summary>
        public PageHost()
        {
            UpdatePseudoClasses();
        }

        /// <summary>Page title. Rendered as the first, top-left text of the page (UI automation finds it there).</summary>
        public string? Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Optional one-line description under the title (wraps when long; hidden when empty).</summary>
        public string? Subtitle
        {
            get => GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        /// <summary>Optional buttons shown on the right of the header; they wrap under the title when the page is narrow. Prefer a <c>WrapPanel</c> for several buttons.</summary>
        public object? HeaderActions
        {
            get => GetValue(HeaderActionsProperty);
            set => SetValue(HeaderActionsProperty, value);
        }

        /// <summary>Optional small element after the actions, e.g. a "PLAYING" pill.</summary>
        public object? HeaderBadge
        {
            get => GetValue(HeaderBadgeProperty);
            set => SetValue(HeaderBadgeProperty, value);
        }

        /// <summary><see cref="PageMode.Reading"/> (default, width-limited and centred) or <see cref="PageMode.Data"/> (full width).</summary>
        public PageMode Mode
        {
            get => GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        /// <summary>Maximum content width in <see cref="PageMode.Reading"/> mode. Default 900.</summary>
        public double ContentMaxWidth
        {
            get => GetValue(ContentMaxWidthProperty);
            set => SetValue(ContentMaxWidthProperty, value);
        }

        /// <summary>
        /// When true (default) the body scrolls vertically. Set false for pages that manage their own
        /// scrolling (e.g. Notes): the content then fills the remaining height.
        /// </summary>
        public bool IsScrollable
        {
            get => GetValue(IsScrollableProperty);
            set => SetValue(IsScrollableProperty, value);
        }

        /// <summary>The max width actually applied to header and body: <see cref="ContentMaxWidth"/> in Reading mode, infinity in Data mode (read-only, used by the template).</summary>
        public double EffectiveContentMaxWidth
        {
            get => _effectiveContentMaxWidth;
            private set => SetAndRaise(EffectiveContentMaxWidthProperty, ref _effectiveContentMaxWidth, value);
        }

        /// <inheritdoc />
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ModeProperty || change.Property == ContentMaxWidthProperty)
            {
                EffectiveContentMaxWidth = Mode == PageMode.Data ? double.PositiveInfinity : ContentMaxWidth;
                UpdatePseudoClasses();
            }
            else if (change.Property == IsScrollableProperty)
            {
                UpdatePseudoClasses();
            }
        }

        /// <inheritdoc />
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            PseudoClasses.Set(":narrow", e.NewSize.Width < NarrowWidth);
        }

        private void UpdatePseudoClasses()
        {
            PseudoClasses.Set(":scrollable", IsScrollable);
            PseudoClasses.Set(":reading", Mode == PageMode.Reading);
            PseudoClasses.Set(":data", Mode == PageMode.Data);
        }
    }
}
