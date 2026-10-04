namespace CenterHubNew.MVVM.Navigation
{
    /// <summary>What the main window offers to the rest of the app. Implemented by <c>MainViewModel</c>.</summary>
    public interface IShellHost
    {
        void NavigateTo(string pageKey);
        void ShowCommandPalette();
        void ShowSetupWizard();
        void CloseOverlay();
        void ZoomBy(double delta);
        void ResetZoom();
        void ToggleSidebar();
    }

    /// <summary>
    /// DI singleton through which pages, cards, the status strip, the command palette and the
    /// settings page drive the shell — without referencing MainViewModel (which owns them) and
    /// without a circular DI dependency. Calls are ignored until the shell has attached.
    /// All calls must be made on the UI thread.
    /// </summary>
    public sealed class ShellService
    {
        private IShellHost? _host;

        /// <summary>Called once by the shell when it is created.</summary>
        public void Attach(IShellHost host) => _host = host;

        public bool IsAttached => _host != null;

        public void NavigateTo(string pageKey) => _host?.NavigateTo(pageKey);
        public void ShowCommandPalette() => _host?.ShowCommandPalette();
        public void ShowSetupWizard() => _host?.ShowSetupWizard();
        public void CloseOverlay() => _host?.CloseOverlay();
        public void ZoomBy(double delta) => _host?.ZoomBy(delta);
        public void ResetZoom() => _host?.ResetZoom();
        public void ToggleSidebar() => _host?.ToggleSidebar();
    }
}
