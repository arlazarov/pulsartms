using Application.Features.Routing.Services.Routes;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class PlanningWriteOwnershipTests
{
  [Fact]
  public void PlanningStoresDoNotExposeIndependentProfileOrFuelWrites()
  {
    Assert.Null(typeof(TruckPlanningProfileService).GetMethod("SaveAsync"));
    Assert.Null(typeof(RoutePlanStore).GetMethod("StoreFuelAsync"));
    foreach (
      var name in new[]
      {
        "StoreFuelRouteAsync",
        "StoreFuelAsync",
        "ClearFuelAsync",
        "StoreRecommendationsAsync",
      }
    )
      Assert.Null(typeof(RoutePlanningService).GetMethod(name));
    Assert.Null(typeof(RoutePlanStore).GetMethod("ClearFuelAsync"));
    Assert.Null(typeof(RoutePlanStore).GetMethod("StoreRecommendationsAsync"));
  }
}
