using System.Net;
using Bunit;
using Client.Models;
using Client.Shared.Measurements.DistanceText;
using Microsoft.AspNetCore.Components;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DistanceTextTests
{
  [Theory]
  [InlineData("both", "100\u00a0mi · 161\u00a0km")]
  [InlineData("miles", "100\u00a0mi")]
  [InlineData("kilometers", "161\u00a0km")]
  public void UnitsStayAttachedToTheirNumbers(string unit, string expected)
  {
    using var context = new BunitContext();
    var cut = context.Render<CascadingValue<DisplayUnits>>(p =>
      p.Add(x => x.Value, new DisplayUnits(Distance: unit))
        .AddChildContent<DistanceText>(child => child.Add(x => x.Miles, 100))
    );

    Assert.Equal(expected, WebUtility.HtmlDecode(cut.Markup).Trim());
  }

  [Fact]
  public void MissingDistanceRetainsItsNeutralPlaceholder()
  {
    using var context = new BunitContext();
    var cut = context.Render<DistanceText>();

    Assert.Equal("—", WebUtility.HtmlDecode(cut.Markup).Trim());
  }
}
