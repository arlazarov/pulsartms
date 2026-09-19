using Client.Models;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DisplayUnitsTests
{
  [Theory]
  [InlineData("miles", "1,877 mi")]
  [InlineData("kilometers", "3,021 km")]
  [InlineData("both", "1,877 mi · 3,021 km")]
  public void DistanceConvertsOnlyForDisplay(string unit, string expected)
  {
    var units = new DisplayUnits(Distance: unit);
    Assert.Equal(expected, units.FormatDistance(1877));
    Assert.Equal("—", units.FormatDistance(null));
    Assert.Equal("—", units.FormatDistance(double.NaN));
    Assert.Equal("—", units.FormatDistance(-1));
  }

  [Theory]
  [InlineData("fahrenheit", 26.5, "79.7", "°F")]
  [InlineData("celsius", 26.5, "26.5", "°C")]
  [InlineData("both", 0, "0", "°C")]
  [InlineData("celsius", 0, "0", "°C")]
  [InlineData("fahrenheit", -40, "-40", "°F")]
  public void TemperatureKeepsZeroAndNegativeReadings(
    string unit,
    double value,
    string expected,
    string label
  )
  {
    var units = new DisplayUnits(Temperature: unit);
    Assert.Equal(expected, units.TemperatureValue((decimal)value));
    Assert.Equal(label, units.TemperatureUnit);
    Assert.Equal("—", units.TemperatureValue(null));
  }

  [Fact]
  public void InvalidPreferencesUseDefaultsAndRouteDeltasRetainTheirDirection()
  {
    Assert.Equal(
      DisplayUnits.Default,
      new DisplayUnits("bad", "bad").Normalize()
    );
    Assert.Equal(
      "−16 km",
      new DisplayUnits(Distance: "kilometers").FormatDistanceDelta(-10)
    );
    Assert.Equal("+0 mi · 0 km", DisplayUnits.Default.FormatDistanceDelta(0));
  }

  [Fact]
  public void DefaultAndLegacyTemperatureUseCelsiusButPreserveFahrenheit()
  {
    Assert.Equal("celsius", DisplayUnits.Default.Temperature);
    Assert.Equal("celsius", new DisplayUnits("both").Normalize().Temperature);
    Assert.Equal(
      "fahrenheit",
      new DisplayUnits("fahrenheit").Normalize().Temperature
    );
    Assert.Equal("both", new DisplayUnits("both").Normalize().Distance);
  }
}
