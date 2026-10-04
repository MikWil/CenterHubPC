using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    // Code-behind is limited to what XAML can't express: focusing the search box, scrolling the
    // highlighted row into view, and forwarding Up / Down / Enter to the view-model.
    public partial class CommandPaletteView : UserControl
    {
        private CommandPaletteViewModel? _vm;

        public CommandPaletteView()
        {
            InitializeComponent();

            // Tunnel, so the TextBox can't swallow the arrow keys first.
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
            DataContextChanged += (_, _) => Attach();
            AttachedToVisualTree += (_, _) =>
            {
                Attach();
                FocusSearch();
            };
            DetachedFromVisualTree += (_, _) => Detach();
        }

        private void Attach()
        {
            var vm = DataContext as CommandPaletteViewModel;
            if (ReferenceEquals(vm, _vm)) return;

            Detach();
            _vm = vm;
            if (_vm is null) return;
            _vm.FocusRequested += FocusSearch;
            _vm.ScrollToRequested += ScrollTo;
        }

        private void Detach()
        {
            if (_vm is null) return;
            _vm.FocusRequested -= FocusSearch;
            _vm.ScrollToRequested -= ScrollTo;
            _vm = null;
        }

        private void FocusSearch()
        {
            Dispatcher.UIThread.Post(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }

        private void ScrollTo(int index)
        {
            Dispatcher.UIThread.Post(
                () => ResultsList.ContainerFromIndex(index)?.BringIntoView(),
                DispatcherPriority.Background);
        }

        private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            var vm = _vm;
            if (vm is null || e.KeyModifiers != KeyModifiers.None) return;

            switch (e.Key)
            {
                case Key.Down:
                    vm.MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    vm.MoveSelection(5);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    vm.MoveSelection(-5);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    _ = vm.RunSelectedAsync();
                    e.Handled = true;
                    break;
            }
        }
    }
}
