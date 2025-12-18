using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;

namespace CenterHubNew.MVVM.ViewModel
{
    public abstract partial class BaseViewModel : ObservableObject, IDisposable
    {
        protected readonly ILogger? Logger;
        private bool _disposed;

        protected BaseViewModel(ILogger? logger = null)
        {
            Logger = logger;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Override in derived classes to dispose resources
                Logger?.LogInformation("Disposing {ViewModelType}", GetType().Name);
            }
            _disposed = true;
        }

        protected bool IsDisposed => _disposed;

        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }
}
