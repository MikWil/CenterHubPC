using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CenterHubNew.MVVM.Services;

namespace CenterHubNew.MVVM.View
{
    public partial class NotesView : UserControl
    {
        private bool _isUpdatingContent = false;
        private MVVM.ViewModel.QuickNotesViewModel? _viewModel;

        public NotesView()
        {
            InitializeComponent();
            DataContextChanged += NotesView_DataContextChanged;
            Loaded += NotesView_Loaded;
        }

        private void NotesView_Loaded(object sender, RoutedEventArgs e)
        {
            // Initial load of content if there's a selected note
            if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.CurrentContent))
            {
                LoadContentToEditor(_viewModel.CurrentContent);
            }
        }

        private void NotesView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old viewmodel
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _viewModel = DataContext as MVVM.ViewModel.QuickNotesViewModel;
            
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                
                // Load current content if any
                if (!string.IsNullOrEmpty(_viewModel.CurrentContent))
                {
                    LoadContentToEditor(_viewModel.CurrentContent);
                }
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MVVM.ViewModel.QuickNotesViewModel.CurrentContent) && !_isUpdatingContent)
            {
                if (_viewModel != null)
                {
                    LoadContentToEditor(_viewModel.CurrentContent);
                }
            }
        }

        private void LoadContentToEditor(string content)
        {
            if (ContentEditor == null) return;
            
            _isUpdatingContent = true;
            try
            {
                ContentEditor.Document.Blocks.Clear();
                if (!string.IsNullOrEmpty(content))
                {
                    // Try to load as RTF first
                    if (content.TrimStart().StartsWith("{\\rtf"))
                    {
                        try
                        {
                            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                            var range = new TextRange(ContentEditor.Document.ContentStart, ContentEditor.Document.ContentEnd);
                            range.Load(stream, DataFormats.Rtf);
                        }
                        catch
                        {
                            // If RTF parsing fails, load as plain text
                            ContentEditor.Document.Blocks.Add(new Paragraph(new Run(content)));
                        }
                    }
                    else
                    {
                        // Load as plain text
                        ContentEditor.Document.Blocks.Add(new Paragraph(new Run(content)));
                    }
                }
            }
            finally
            {
                _isUpdatingContent = false;
            }
        }

        private string GetEditorContent()
        {
            if (ContentEditor == null) return string.Empty;
            
            using var stream = new MemoryStream();
            var range = new TextRange(ContentEditor.Document.ContentStart, ContentEditor.Document.ContentEnd);
            range.Save(stream, DataFormats.Rtf);
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private void ContentEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingContent || _viewModel == null) return;

            _isUpdatingContent = true;
            try
            {
                _viewModel.CurrentContent = GetEditorContent();
            }
            finally
            {
                _isUpdatingContent = false;
            }
        }

        private void BoldBtn_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentEditor.Selection;
            if (!selection.IsEmpty)
            {
                var currentWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
                var newWeight = (currentWeight != DependencyProperty.UnsetValue && (FontWeight)currentWeight == FontWeights.Bold)
                    ? FontWeights.Normal
                    : FontWeights.Bold;
                selection.ApplyPropertyValue(TextElement.FontWeightProperty, newWeight);
            }
            ContentEditor.Focus();
        }

        private void ItalicBtn_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentEditor.Selection;
            if (!selection.IsEmpty)
            {
                var currentStyle = selection.GetPropertyValue(TextElement.FontStyleProperty);
                var newStyle = (currentStyle != DependencyProperty.UnsetValue && (FontStyle)currentStyle == FontStyles.Italic)
                    ? FontStyles.Normal
                    : FontStyles.Italic;
                selection.ApplyPropertyValue(TextElement.FontStyleProperty, newStyle);
            }
            ContentEditor.Focus();
        }

        private void UnderlineBtn_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentEditor.Selection;
            if (!selection.IsEmpty)
            {
                var currentDecoration = selection.GetPropertyValue(Inline.TextDecorationsProperty);
                var newDecoration = (currentDecoration != DependencyProperty.UnsetValue && currentDecoration == TextDecorations.Underline)
                    ? null
                    : TextDecorations.Underline;
                selection.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecoration);
            }
            ContentEditor.Focus();
        }

        private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontSizeCombo.SelectedItem is ComboBoxItem item && ContentEditor != null)
            {
                if (double.TryParse(item.Content?.ToString(), out double size))
                {
                    var selection = ContentEditor.Selection;
                    if (!selection.IsEmpty)
                    {
                        selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                    }
                    ContentEditor.Focus();
                }
            }
        }

        private void ColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorHex)
            {
                var selection = ContentEditor.Selection;
                if (!selection.IsEmpty)
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                    selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                }
                ContentEditor.Focus();
            }
        }

        private void BulletBtn_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleBullets.Execute(null, ContentEditor);
            ContentEditor.Focus();
        }

        private void NumberBtn_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleNumbering.Execute(null, ContentEditor);
            ContentEditor.Focus();
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Rich Text Format (*.rtf)|*.rtf|Text Files (*.txt)|*.txt",
                DefaultExt = ".rtf"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var range = new TextRange(ContentEditor.Document.ContentStart, ContentEditor.Document.ContentEnd);
                    using var stream = new FileStream(dialog.FileName, FileMode.Create);
                    var format = dialog.FileName.EndsWith(".rtf") ? DataFormats.Rtf : DataFormats.Text;
                    range.Save(stream, format);
                    ToastService.Instance.Success($"Note exported to: {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    ToastService.Instance.Error($"Failed to export note: {ex.Message}");
                }
            }
        }

        private void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Rich Text Format (*.rtf)|*.rtf|Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using var stream = new FileStream(dialog.FileName, FileMode.Open);
                    var range = new TextRange(ContentEditor.Document.ContentStart, ContentEditor.Document.ContentEnd);
                    var format = dialog.FileName.EndsWith(".rtf") ? DataFormats.Rtf : DataFormats.Text;
                    range.Load(stream, format);
                    
                    // Update the viewmodel with the imported content
                    if (_viewModel != null)
                    {
                        _viewModel.CurrentContent = GetEditorContent();
                    }
                    ToastService.Instance.Success($"Note imported from: {Path.GetFileName(dialog.FileName)}");
                }
                catch (Exception ex)
                {
                    ToastService.Instance.Error($"Failed to import note: {ex.Message}");
                }
            }
        }
    }
}
