using Avalonia.Controls;
using Avalonia.Interactivity;
using CenterHubNew.MVVM.Services;
using System;
using System.IO;

namespace CenterHubNew.MVVM.View
{
    public partial class NotesView : UserControl
    {
        public NotesView()
        {
            InitializeComponent();
        }

        private void ExportBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModel.QuickNotesViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.CurrentContent)) return;

            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = vm.CurrentTitle ?? "note"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, vm.CurrentContent);
                    ToastService.Instance.Success($"Exported to: {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    ToastService.Instance.Error($"Export failed: {ex.Message}");
                }
            }
        }
    }
}
