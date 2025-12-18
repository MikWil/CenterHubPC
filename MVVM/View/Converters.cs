using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CenterHubNew.MVVM.View
{
    public class TempToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is float f && f >= 0)
            {
                if (parameter?.ToString() == "Max")
                    return $"Max {f:F1}°C";
                return $"{f:F1}°C";
            }
            return parameter?.ToString() == "Max" ? "Max N/A" : "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class VramToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long l && l > 0)
                return $"{l} GB VRAM";
            return "VRAM N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToMuteTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "Unmute" : "Mute";
            return "Mute";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Commented out unused converter to avoid XAML errors
    // public class SliderValueToWidthConverter : IValueConverter
    // {
    //     public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    //     {
    //         // value: current Value, parameter: Maximum
    //         if (value is double val && parameter is string maxStr && double.TryParse(maxStr, out double max) && max > 0)
    //         {
    //             // The width will be set by the parent, so just return the percentage
    //             return val / max * 120.0; // 120 is the default slider width, adjust as needed
    //         }
    //         return 0.0;
    //     }
    //
    //     public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    // }

    public class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 3 &&
                values[0] is double value &&
                values[1] is double maximum &&
                values[2] is double actualWidth &&
                !double.IsNaN(actualWidth) &&
                actualWidth > 0 &&
                maximum > 0)
            {
                return (value / maximum) * actualWidth;
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToMonitoringTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "⏸ Pause" : "▶ Resume";
            return "▶ Resume";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToPinBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(0, 212, 255)); // Accent color
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToRunningTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "RUNNING" : "STOPPED";
            return "STOPPED";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToRunningBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Green
            return new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return false;
        }
    }

    public class ToastTypeToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Services.ToastType type)
            {
                return type switch
                {
                    Services.ToastType.Success => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                    Services.ToastType.Error => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                    Services.ToastType.Warning => new SolidColorBrush(Color.FromRgb(241, 196, 15)),
                    _ => new SolidColorBrush(Color.FromRgb(52, 152, 219))
                };
            }
            return new SolidColorBrush(Color.FromRgb(52, 152, 219));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ToastTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Services.ToastType type)
            {
                return type switch
                {
                    Services.ToastType.Success => "✓",
                    Services.ToastType.Error => "✕",
                    Services.ToastType.Warning => "⚠",
                    _ => "ℹ"
                };
            }
            return "ℹ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
} 