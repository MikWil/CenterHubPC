using System;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>
    /// A view-model shown in the main window's full-window overlay layer (command palette, audio
    /// setup wizard). The shell shows it with <c>MainViewModel.ShowOverlay</c>, closes it on Esc or a
    /// click on the dimmed backdrop (when <see cref="CloseOnBackdropClick"/>), and listens to
    /// <see cref="CloseRequested"/> for the overlay closing itself.
    /// </summary>
    public interface IOverlayViewModel
    {
        /// <summary>Raise to ask the shell to close this overlay.</summary>
        event Action? CloseRequested;

        /// <summary>Whether a click on the dimmed area around the overlay closes it.</summary>
        bool CloseOnBackdropClick { get; }

        /// <summary>Called by the shell each time the overlay is shown (reset state, focus the search box…).</summary>
        void OnShown();

        /// <summary>Called by the shell after the overlay was closed (Esc, backdrop, or CloseRequested).</summary>
        void OnClosed();
    }
}
