using Bunit;
using Client.Shared;
using Client.Shared.Fuel;
using Client.Shared.Fuel.FuelReading;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class FuelReadingTests
{
  [Theory]
  [InlineData(28d, "28%", "is-low")]
  [InlineData(0d, "0%", "is-critical")]
  [InlineData(15d, "15%", "is-critical")]
  [InlineData(16d, "16%", "is-low")]
  [InlineData(30d, "30%", "is-low")]
  [InlineData(31d, "31%", "is-normal")]
  [InlineData(49d, "49%", "is-normal")]
  [InlineData(50d, "50%", "is-normal")]
  [InlineData(100d, "100%", "is-normal")]
  [InlineData(null, "—", "is-unknown")]
  public void PumpPillFormatsTheGivenReadingWithoutInventingAMissingValue(
    double? percent,
    string value,
    string tone
  )
  {
    using var context = new BunitContext();
    var component = context.Render<FuelReading>(p =>
      p.Add(reading => reading.Percent, percent)
    );
    Assert.Equal(value, component.Find(".fuel-reading__value").TextContent);
    Assert.Contains(tone, component.Find(".fuel-reading").ClassList);
    Assert.Equal(
      "true",
      component.Find(".fuel-reading__icon").GetAttribute("aria-hidden")
    );
    Assert.Empty(component.FindAll("button, a, input"));
    Assert.DoesNotContain(
      "fuel-reading--metric",
      component.Find(".fuel-reading").ClassList
    );
  }

  [Theory]
  [InlineData(28d, "28%", "is-low")]
  [InlineData(50d, "50%", "is-normal")]
  [InlineData(100d, "100%", "is-normal")]
  [InlineData(null, "—", "is-unknown")]
  public void MetricVariantPreservesTheReadingAndToneWithASeparateLabel(
    double? percent,
    string value,
    string tone
  )
  {
    using var context = new BunitContext();
    var component = context.Render<FuelReading>(p =>
      p.Add(reading => reading.Percent, percent)
        .Add(reading => reading.Variant, "metric")
    );
    Assert.Contains(
      "fuel-reading--metric",
      component.Find(".fuel-reading").ClassList
    );
    Assert.Contains(tone, component.Find(".fuel-reading").ClassList);
    Assert.Equal("Fuel", component.Find(".fuel-reading__label").TextContent);
    Assert.Equal(value, component.Find(".fuel-reading__value").TextContent);
    Assert.Equal(
      "true",
      component.Find(".fuel-reading__icon").GetAttribute("aria-hidden")
    );
    Assert.Empty(component.FindAll("button, a, input"));
  }
}
