using Application.Features.Dispatch.Models;

namespace Server.Tests.Dispatch;

[Trait("Category", "Finance")]
public sealed class DispatchMileageTests
{
  [Theory]
  [InlineData(1882.86, 160.819, 2043.679)]
  [InlineData(100d, 0d, 100d)]
  [InlineData(100d, null, null)]
  [InlineData(null, 20d, null)]
  [InlineData(null, null, null)]
  public void TotalUsesUnroundedMilesAndRequiresBothDistances(
    double? loaded,
    double? empty,
    double? total
  )
  {
    var response = new DispatchResponse
    {
      LoadedMiles = (decimal?)loaded,
      EmptyMiles = (decimal?)empty,
    };
    Assert.Equal((decimal?)total, response.TotalMiles);
  }
}
