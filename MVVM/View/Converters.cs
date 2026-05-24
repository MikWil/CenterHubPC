using Avalonia.Data.Converters;
using Avalonia.Media;
using CenterHubNew.MVVM.Services;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CenterHubNew.MVVM.View
{
    public class TempToDisplayConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is float f && f >= 0)
            {
                if (parameter?.ToString() == "Max")
                    return $"Max {f:F1}°C";
                return $"{f:F1}°C";
            }
            return parameter?.ToString() == "Max" ? "Max N/A" : "N/A";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class VramToDisplayConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is long l && l > 0)
                return $"{l} GB VRAM";
            return "VRAM N/A";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToMuteTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "Unmute" : "Mute";
            return "Mute";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToMonitoringTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "⏸ Pause" : "▶ Resume";
            return "▶ Resume";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToPinBorderConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(0, 212, 255));
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToRunningTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "RUNNING" : "STOPPED";
            return "STOPPED";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToRunningBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(46, 204, 113));
            return new SolidColorBrush(Color.FromRgb(231, 76, 60));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }

    public class ToastTypeToBackgroundConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ToastType type)
            {
                return type switch
                {
                    ToastType.Success => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                    ToastType.Error   => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                    ToastType.Warning => new SolidColorBrush(Color.FromRgb(241, 196, 15)),
                    _                 => new SolidColorBrush(Color.FromRgb(52, 152, 219))
                };
            }
            return new SolidColorBrush(Color.FromRgb(52, 152, 219));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ToastTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ToastType type)
            {
                return type switch
                {
                    ToastType.Success => "✓",
                    ToastType.Error   => "✕",
                    ToastType.Warning => "⚠",
                    _                 => "ℹ"
                };
            }
            return "ℹ";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                bool invert = parameter?.ToString() == "Invert";
                bool hasItems = count > 0;
                return invert ? !hasItems : hasItems;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
