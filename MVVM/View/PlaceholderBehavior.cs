using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CenterHubNew.MVVM.View;

public static class PlaceholderBehavior
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached(
            "Placeholder",
            typeof(string),
            typeof(PlaceholderBehavior),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public static string GetPlaceholder(DependencyObject obj) => (string)obj.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject obj, string value) => obj.SetValue(PlaceholderProperty, value);

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            textBox.GotFocus -= TextBox_GotFocus;
            textBox.LostFocus -= TextBox_LostFocus;
            textBox.Loaded -= TextBox_Loaded;

            if (!string.IsNullOrEmpty((string)e.NewValue))
            {
                textBox.GotFocus += TextBox_GotFocus;
                textBox.LostFocus += TextBox_LostFocus;
                textBox.Loaded += TextBox_Loaded;
            }
        }
    }

    private static void TextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            UpdatePlaceholder(textBox);
    }

    private static void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            var placeholder = GetPlaceholder(textBox);
            if (textBox.Text == placeholder)
            {
                textBox.Text = string.Empty;
                textBox.Foreground = new SolidColorBrush(Color.FromRgb(232, 244, 248));
            }
        }
    }

    private static void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            UpdatePlaceholder(textBox);
    }

    private static void UpdatePlaceholder(TextBox textBox)
    {
        var placeholder = GetPlaceholder(textBox);
        if (string.IsNullOrEmpty(textBox.Text))
        {
            textBox.Text = placeholder;
            textBox.Foreground = new SolidColorBrush(Color.FromRgb(160, 180, 200));
        }
        else if (textBox.Text != placeholder)
        {
            textBox.Foreground = new SolidColorBrush(Color.FromRgb(232, 244, 248));
        }
    }
}

