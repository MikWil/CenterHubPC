namespace CenterHubNew.MVVM.Controls
{
    /// <summary>How a <see cref="PageHost"/> sizes its content horizontally.</summary>
    public enum PageMode
    {
        /// <summary>Reading page: content is limited to <see cref="PageHost.ContentMaxWidth"/> (default 900) and centred.</summary>
        Reading,

        /// <summary>Data page: content uses the full width of the window.</summary>
        Data
    }
}
