using System.Collections.ObjectModel;

namespace CenterHubNew.MVVM.ViewModel
{
    /// <summary>A sidebar section: a header and its pages.</summary>
    public sealed class NavGroupViewModel
    {
        public NavGroupViewModel(string header)
        {
            Header = header;
        }

        /// <summary>Section name as written in the registry ("Productivity").</summary>
        public string Header { get; }

        /// <summary>What the sidebar shows — headers are uppercase.</summary>
        public string DisplayHeader => Header.ToUpperInvariant();

        public ObservableCollection<NavItemViewModel> Items { get; } = new();
    }
}
