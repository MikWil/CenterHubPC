using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    public partial class SettingsView : UserControl
    {
        private static readonly FilePickerFileType ZipFiles = new("Zip archive") { Patterns = new[] { "*.zip" } };

        public SettingsView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // The Windows startup entry can change outside the app; re-read it whenever the page is shown.
            (DataContext as SettingsViewModel)?.RefreshCommand.Execute(null);
        }

        // The file pickers need the view's TopLevel, so they live here; the chosen path goes to the view-model.

        private async void BackupButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm) return;
            try
            {
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storage is null) return;

                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Back up CenterHub settings",
                    SuggestedFileName = $"CenterHub-settings-{DateTime.Now:yyyy-MM-dd}.zip",
                    DefaultExtension = "zip",
                    FileTypeChoices = new[] { ZipFiles },
                    ShowOverwritePrompt = true,
                });

                var path = file?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) vm.BackUpToCommand.Execute(path);
            }
            catch (Exception ex)
            {
                ToastService.Instance.Error($"Couldn't open the save dialog: {ex.Message}");
            }
        }

        private async void RestoreButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm) return;
            try
            {
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storage is null) return;

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Restore CenterHub settings from a backup",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { ZipFiles },
                });

                var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                if (!string.IsNullOrEmpty(path)) vm.RestoreFromCommand.Execute(path);
            }
            catch (Exception ex)
            {
                ToastService.Instance.Error($"Couldn't open the file dialog: {ex.Message}");
            }
        }
    }
}
