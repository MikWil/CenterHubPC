using System.Globalization;
using CenterHubNew.MVVM.Converters;
using Xunit;

namespace CenterHubNew.Tests;

public class ConverterTests
{
    private readonly PercentToWidthConverter _toWidth = new();
    private readonly TempToDisplayConverter _temp = new();

    [Theory]
    [InlineData(15.8, 14.9)]   // C:\ at 15.8% of a 94px meter — used to render empty
    [InlineData(0.0, 0.0)]
    [InlineData(100.0, 94.0)]
    [InlineData(150.0, 94.0)]  // clamped
    [InlineData(-5.0, 0.0)]    // clamped
    public void Percent_maps_to_meter_width(double percent, double expected)
        => Assert.Equal(expected, (double)_toWidth.Convert(percent, typeof(double), "94", CultureInfo.InvariantCulture)!);

    [Fact]
    public void NaN_percent_renders_an_empty_meter()
        => Assert.Equal(0.0, (double)_toWidth.Convert(double.NaN, typeof(double), "94", CultureInfo.InvariantCulture)!);

    [Fact]
    public void Unavailable_temperature_shows_NA_not_zero()
        => Assert.Equal("N/A", _temp.Convert(-1f, typeof(string), null, CultureInfo.InvariantCulture));
}
