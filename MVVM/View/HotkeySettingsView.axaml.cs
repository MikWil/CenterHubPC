using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    public partial class HotkeySettingsView : UserControl
    {
        private TopLevel? _topLevel;

        public HotkeySettingsView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // Listen for key presses at the window root. Key events bubble up to the
            // TopLevel no matter which control currently has focus, so recording works
            // from a single click on the record button — the user no longer has to
            // keep the mouse button held down to keep focus on the field.
            // handledEventsToo: true so we still see keys a focused control consumed.
            _topLevel = TopLevel.GetTopLevel(this);
            _topLevel?.AddHandler(KeyDownEvent, OnGlobalKeyDown,
                RoutingStrategies.Bubble, handledEventsToo: true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _topLevel?.RemoveHandler(KeyDownEvent, OnGlobalKeyDown);
            _topLevel = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not HotkeySettingsViewModel vm) return;
            if (vm.RecordingBinding == null) return; // only intercept while recording

            vm.HandleKeyDown(e.Key, e.KeyModifiers);
            e.Handled = true;
        }
    }
}
