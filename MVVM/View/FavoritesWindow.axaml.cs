using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CenterHubNew.MVVM.ViewModel;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.View
{
    public partial class FavoritesWindow : Window
    {
        private static FavoritesWindow? _instance;

        // After a drag has stopped, a window within this many DIPs of a screen edge snaps to it (so it lands in a corner).
        private const double SnapDistance = 28;
        private const double SnapMargin = 8;

        private readonly DispatcherTimer _snapTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private bool _snapping;

        public static void ShowSingleton()
        {
            if (_instance == null)
            {
                try
                {
                    _instance = new FavoritesWindow();
                    _instance.Closed += (s, e) => _instance = null;
                    _instance.Show();
                    _instance.Activate();
                }
                catch
                {
                    _instance = null;
                }
            }
            else
            {
                _instance.Activate();
            }
        }

        public FavoritesWindow(FavoritesViewModel? viewModel = null, ILogger<FavoritesWindow>? logger = null)
        {
            InitializeComponent();

            try
            {
                var vm = viewModel ?? App.Services.GetService(typeof(FavoritesViewModel)) as FavoritesViewModel;
                DataContext = vm;
                if (vm != null)
                    Closed += (_, _) => vm.Dispose();
            }
            catch
            {
                // Window will have no DataContext if DI fails
            }

            _snapTimer.Tick += (_, _) => SnapToScreenEdges();
            PositionChanged += (_, _) =>
            {
                if (_snapping) return;
                _snapTimer.Stop();
                _snapTimer.Start(); // restart: snap once the window has stopped moving
            };
            Closed += (_, _) => _snapTimer.Stop();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void ResizeRight_PointerPressed(object? sender, PointerPressedEventArgs e) => Resize(WindowEdge.East, e);

        private void ResizeBottom_PointerPressed(object? sender, PointerPressedEventArgs e) => Resize(WindowEdge.South, e);

        private void ResizeCorner_PointerPressed(object? sender, PointerPressedEventArgs e) => Resize(WindowEdge.SouthEast, e);

        private void Resize(WindowEdge edge, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginResizeDrag(edge, e);
        }

        /// <summary>Snaps to the nearest screen edge on each axis when it is close, so the window settles in a corner.</summary>
        private void SnapToScreenEdges()
        {
            _snapTimer.Stop();
            try
            {
                var screen = Screens.ScreenFromVisual(this);
                if (screen == null) return;

                double scale = screen.Scaling;
                var area = screen.WorkingArea;
                int width = (int)Math.Round(Bounds.Width * scale);
                int height = (int)Math.Round(Bounds.Height * scale);
                int snap = (int)Math.Round(SnapDistance * scale);
                int margin = (int)Math.Round(SnapMargin * scale);

                int x = SnapAxis(Position.X, width, area.X, area.Right, snap, margin);
                int y = SnapAxis(Position.Y, height, area.Y, area.Bottom, snap, margin);
                if (x == Position.X && y == Position.Y) return;

                _snapping = true;
                try { Position = new PixelPoint(x, y); }
                finally { _snapping = false; }
            }
            catch
            {
                // Snapping is cosmetic: never let it break the window.
            }
        }

        private static int SnapAxis(int pos, int size, int min, int max, int snap, int margin)
        {
            if (Math.Abs(pos - min) <= snap) return min + margin;
            if (Math.Abs(pos + size - max) <= snap) return max - size - margin;
            return pos;
        }
    }
}
