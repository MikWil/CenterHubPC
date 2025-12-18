using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class MoveFilesViewModel : BaseViewModel
    {
        [ObservableProperty]
        private string? sourceLocation;

        [ObservableProperty]
        private string? targetLocation;

        [ObservableProperty]
        private bool isCopy = false;

        [ObservableProperty]
        private string statusMessage = "";

        public MoveFilesViewModel(ILogger<MoveFilesViewModel>? logger = null) : base(logger)
        {
            Logger?.LogInformation("MoveFilesViewModel initialized");
        }

        [RelayCommand]
        public void BrowseSource()
        {
            if (IsDisposed) return;

            try
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.Description = "Select source folder";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SourceLocation = dialog.SelectedPath;
                    Logger?.LogDebug("Source location selected: {Path}", SourceLocation);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error browsing source location");
                StatusMessage = $"Error browsing source: {ex.Message}";
            }
        }

        [RelayCommand]
        public void BrowseTarget()
        {
            if (IsDisposed) return;

            try
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.Description = "Select target folder";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TargetLocation = dialog.SelectedPath;
                    Logger?.LogDebug("Target location selected: {Path}", TargetLocation);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error browsing target location");
                StatusMessage = $"Error browsing target: {ex.Message}";
            }
        }

        [RelayCommand]
        public void MoveFiles()
        {
            if (IsDisposed) return;

            try
            {
                if (string.IsNullOrEmpty(SourceLocation) || string.IsNullOrEmpty(TargetLocation))
                {
                    StatusMessage = "Please select both source and target locations.";
                    Logger?.LogWarning("Move files attempted without selecting both locations");
                    return;
                }

                if (!Directory.Exists(SourceLocation))
                {
                    StatusMessage = "Source directory does not exist.";
                    Logger?.LogWarning("Source directory does not exist: {Path}", SourceLocation);
                    return;
                }

                if (!Directory.Exists(TargetLocation))
                {
                    try
                    {
                        Directory.CreateDirectory(TargetLocation);
                        Logger?.LogInformation("Created target directory: {Path}", TargetLocation);
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Could not create target directory: {ex.Message}";
                        Logger?.LogError(ex, "Failed to create target directory: {Path}", TargetLocation);
                        return;
                    }
                }

                var allFiles = Directory.GetFiles(SourceLocation);
                if (!allFiles.Any())
                {
                    StatusMessage = "No files found in source directory.";
                    Logger?.LogWarning("No files found in source directory: {Path}", SourceLocation);
                    return;
                }

                Logger?.LogInformation("Starting file operation - Source: {Source}, Target: {Target}, Operation: {Operation}", 
                    SourceLocation, TargetLocation, IsCopy ? "Copy" : "Move");

                int processedFiles = 0;
                int skippedFiles = 0;
                foreach (var file in allFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        var targetPath = Path.Combine(TargetLocation, fileName);

                        if (File.Exists(targetPath))
                        {
                            var result = MessageBox.Show($"File {fileName} already exists in target. Overwrite?", 
                                "File exists", MessageBoxButton.YesNo);
                            if (result != MessageBoxResult.Yes)
                            {
                                skippedFiles++;
                                Logger?.LogDebug("Skipped file due to user choice: {FileName}", fileName);
                                continue;
                            }
                        }

                        if (IsCopy)
                        {
                            File.Copy(file, targetPath, true);
                            Logger?.LogDebug("Copied file: {FileName}", fileName);
                        }
                        else
                        {
                            File.Move(file, targetPath, true);
                            Logger?.LogDebug("Moved file: {FileName}", fileName);
                        }
                        processedFiles++;
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogError(ex, "Error processing file: {File}", file);
                        StatusMessage = $"Error processing {Path.GetFileName(file)}: {ex.Message}";
                        return;
                    }
                }

                StatusMessage = $"Successfully {(IsCopy ? "copied" : "moved")} {processedFiles} files{(skippedFiles > 0 ? $", skipped {skippedFiles} files" : "")}.";
                Logger?.LogInformation("File operation completed - Processed: {Processed}, Skipped: {Skipped}", processedFiles, skippedFiles);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error during file operation");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Logger?.LogInformation("MoveFilesViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
