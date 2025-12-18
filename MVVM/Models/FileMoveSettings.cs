using CommunityToolkit.Mvvm.ComponentModel;

namespace CenterHubNew.MVVM.Models
{
    public partial class FileMoveSettings : ObservableObject
    {
        [ObservableProperty]
        private string? sourceLocation;

        [ObservableProperty]
        private string? targetLocation;

        [ObservableProperty]
        private bool isCopy = false;

        public FileMoveSettings()
        {
            
        }
    }
} 