using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace CenterHubNew.MVVM.Converters
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

    public class InverseBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }
    }

    public class NotNullConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
