using Application.Features.Dispatch.Services;

namespace Server.Tests.Routing;

[Trait("Category", "Finance")]
public sealed class DispatchRatesTests
{
  [Fact]
  public void ZeroPriceIsValidButUnknownOrZeroDistanceIsNot()
  {
    Assert.Equal(0m, DispatchRates.PerMile(0, 100));
    Assert.Null(DispatchRates.PerMile(100, 0));
    Assert.Null(DispatchRates.PerMile(null, 100));
    Assert.Null(DispatchRates.PerMile(100, null));
    Assert.Null(DispatchRates.PerMile(-1, 100));
    Assert.Equal(0.333333m, DispatchRates.PerMile(1, 3));
  }
}
