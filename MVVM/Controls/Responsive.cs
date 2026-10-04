using Avalonia;
using Avalonia.Controls;

namespace CenterHubNew.MVVM.Controls
{
    /// <summary>
    /// Attached properties that turn a control's own width into pseudo-classes, so a page can restyle
    /// itself with plain style selectors. This replaces XAML container queries, which Avalonia 11.2 does not have.
    /// </summary>
    /// <remarks>
    /// <code>
    /// &lt;UserControl controls:Responsive.NarrowBelow="700" ...&gt;
    ///   &lt;UserControl.Styles&gt;
    ///     &lt;Style Selector="UserControl:narrow StackPanel.row"&gt;
    ///       &lt;Setter Property="Orientation" Value="Vertical"/&gt;
    ///     &lt;/Style&gt;
    ///   &lt;/UserControl.Styles&gt;
    /// </code>
    /// <c>NarrowBelow="700"</c> sets <c>:narrow</c> while the control is narrower than 700;
    /// <c>WideAbove="1100"</c> sets <c>:wide</c> while it is wider than 1100.
    /// </remarks>
    public sealed class Responsive
    {
        /// <summary>Width below which the control gets the <c>:narrow</c> pseudo-class (0 = never).</summary>
        public static readonly AttachedProperty<double> NarrowBelowProperty =
            AvaloniaProperty.RegisterAttached<Responsive, Control, double>("NarrowBelow");

        /// <summary>Width above which the control gets the <c>:wide</c> pseudo-class (0 = never).</summary>
        public static readonly AttachedProperty<double> WideAboveProperty =
            AvaloniaProperty.RegisterAttached<Responsive, Control, double>("WideAbove");

        private Responsive()
        {
        }

        static Responsive()
        {
            NarrowBelowProperty.Changed.AddClassHandler<Control>(OnThresholdChanged);
            WideAboveProperty.Changed.AddClassHandler<Control>(OnThresholdChanged);
        }

        /// <summary>Gets the width below which <c>:narrow</c> is set.</summary>
        public static double GetNarrowBelow(Control control) => control.GetValue(NarrowBelowProperty);

        /// <summary>Sets the width below which <c>:narrow</c> is set.</summary>
        public static void SetNarrowBelow(Control control, double value) => control.SetValue(NarrowBelowProperty, value);

        /// <summary>Gets the width above which <c>:wide</c> is set.</summary>
        public static double GetWideAbove(Control control) => control.GetValue(WideAboveProperty);

        /// <summary>Sets the width above which <c>:wide</c> is set.</summary>
        public static void SetWideAbove(Control control, double value) => control.SetValue(WideAboveProperty, value);

        private static void OnThresholdChanged(Control control, AvaloniaPropertyChangedEventArgs e)
        {
            // Subscribing the same static handler twice is harmless after the -= below.
            control.SizeChanged -= OnSizeChanged;
            control.SizeChanged += OnSizeChanged;
            Apply(control, control.Bounds.Width);
        }

        private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is Control control)
                Apply(control, e.NewSize.Width);
        }

        private static void Apply(Control control, double width)
        {
            var classes = (IPseudoClasses)control.Classes;
            double narrow = GetNarrowBelow(control);
            double wide = GetWideAbove(control);
            classes.Set(":narrow", narrow > 0 && width > 0 && width < narrow);
            classes.Set(":wide", wide > 0 && width > wide);
        }
    }
}
