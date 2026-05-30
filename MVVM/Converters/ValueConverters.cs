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

    /// <summary>
    /// Locale-tolerant double binding. Accepts both '.' and ',' as decimal
    /// separators on input, always displays using InvariantCulture so the
    /// UI is consistent regardless of system locale.
    /// </summary>
    public class FlexibleDoubleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                double d => d.ToString(CultureInfo.InvariantCulture),
                float f  => f.ToString(CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                int i    => i.ToString(CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? string.Empty,
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var s = value?.ToString()?.Trim() ?? string.Empty;
            if (s.Length == 0) return 0d;

            // Normalize: treat both '.' and ',' as decimal separator.
            // Strip thousands separators of either flavour if present.
            // Heuristic: if the string contains exactly one separator (. or ,) treat it as decimal.
            var lastDot = s.LastIndexOf('.');
            var lastComma = s.LastIndexOf(',');
            var decimalSepIdx = Math.Max(lastDot, lastComma);

            string normalized;
            if (decimalSepIdx < 0)
            {
                normalized = s;
            }
            else
            {
                var intPart = s.Substring(0, decimalSepIdx).Replace(".", "").Replace(",", "");
                var fracPart = s.Substring(decimalSepIdx + 1);
                normalized = intPart + "." + fracPart;
            }

            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;

            // Last resort — try parsing with the original culture
            if (double.TryParse(s, NumberStyles.Float, culture, out d))
                return d;

            return Avalonia.Data.BindingOperations.DoNothing;
        }
    }

    /// <summary>
    /// Same as FlexibleDoubleConverter but for integers.
    /// </summary>
    public class FlexibleIntConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value?.ToString() ?? string.Empty;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var s = value?.ToString()?.Trim() ?? string.Empty;
            if (s.Length == 0) return 0;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;
            if (int.TryParse(s, NumberStyles.Integer, culture, out i))
                return i;

            return Avalonia.Data.BindingOperations.DoNothing;
        }
    }
}
