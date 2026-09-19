using Bunit;
using Client.Shared.Brand.BrandLogo;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class BrandLogoTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task EveryVariantPreservesTheAccessibleCanonicalWordmark(
    bool reversed,
    bool prominent
  )
  {
    await using var context = new BunitContext();
    var component = context.Render<BrandLogo>(parameters =>
      parameters
        .Add(value => value.Reversed, reversed)
        .Add(value => value.Prominent, prominent)
    );

    var svg = Assert.Single(component.FindAll("svg"));
    Assert.Equal("img", svg.GetAttribute("role"));
    Assert.Equal("PulsR TMS", svg.GetAttribute("aria-label"));
    Assert.Equal("false", svg.GetAttribute("focusable"));
    Assert.Equal("0 0 480 104", svg.GetAttribute("viewBox"));
    Assert.True(svg.ClassList.Contains("brand-logo"));
    Assert.Equal(reversed, svg.ClassList.Contains("brand-logo--reversed"));
    Assert.Equal(prominent, svg.ClassList.Contains("brand-logo--prominent"));
    Assert.Equal(
      "brand/pulsr.svg?v=pulse-red-2#wordmark",
      Assert.Single(component.FindAll("use")).GetAttribute("href")
    );
    Assert.Empty(component.FindAll("text, image"));
  }
}
