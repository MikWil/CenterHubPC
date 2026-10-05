using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CenterHubNew.MVVM.Services;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    public partial class MetronomeView : UserControl
    {
        private static readonly FilePickerFileType WavFiles = new("WAV audio") { Patterns = new[] { "*.wav" } };

        private bool _devicesRequested;

        public MetronomeView()
        {
            InitializeComponent();
        }

        // The looper's input list is read the first time the dropdown opens (enumeration can be slow).
        private void LooperDevices_DropDownOpened(object? sender, EventArgs e)
        {
            if (_devicesRequested || DataContext is not MetronomeViewModel vm) return;
            _devicesRequested = true;
            vm.RefreshLooperDevicesCommand.Execute(null);
        }

        // The save picker needs the view's TopLevel, so it lives here; the chosen path goes to the view-model.
        private async void LooperExport_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MetronomeViewModel vm) return;
            try
            {
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storage is null) return;

                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export the loop",
                    SuggestedFileName = vm.LooperExportFileName,
                    DefaultExtension = "wav",
                    FileTypeChoices = new[] { WavFiles },
                    ShowOverwritePrompt = true,
                });

                var path = file?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) vm.ExportLoop(path);
            }
            catch (Exception ex)
            {
                ToastService.Instance.Error($"Couldn't open the save dialog: {ex.Message}");
            }
        }
    }
}
