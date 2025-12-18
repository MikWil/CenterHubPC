using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class NameInputViewModel : BaseViewModel
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _displayMessage = "Enter your name:";

        public NameInputViewModel(ILogger<NameInputViewModel>? logger = null) : base(logger)
        {
            Logger?.LogInformation("NameInputViewModel initialized");
        }

        [RelayCommand]
        private void SaveName()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                DisplayMessage = "Please enter a valid name.";
                Logger?.LogWarning("Attempted to save empty name");
                return;
            }

            DisplayMessage = $"Hello, {Name}! Your name has been saved.";
            Logger?.LogInformation("Name saved: {Name}", Name);
        }

        [RelayCommand]
        private void ClearName()
        {
            Name = string.Empty;
            DisplayMessage = "Enter your name:";
            Logger?.LogDebug("Name cleared");
        }
    }
}
