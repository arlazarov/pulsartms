using Domain.Models.Routing;
using Domain.Rules.Routing;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteMetadataCompatibilityTests
{
  [Fact]
  public void MetadataPreservesLegacySignatureCompatibility()
  {
    var load = RouteWorkProjection.Capture(
      new DispatchEntity
      {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        TruckId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
      }
    );
    var profile = new TruckRouteProfile();
    var saved = new SavedRoutePlanMetadata(
      "3C67FB52A95D931E631E4A4E219E96A192F85382D24CFDE9599840A8AC3629D8",
      load.TruckId!.Value,
      load.TruckId.Value,
      Guid.NewGuid(),
      1,
      new() { AllStopsPassed = true },
      null
    )
    {
      PlanDispatchId = load.Id,
      DispatchId = load.Id,
    };
    Assert.True(PlanningWorkPolicy.IsCompleted(saved, load, profile));
    Assert.False(
      PlanningWorkPolicy.IsCompleted(
        saved,
        load with
        {
          RouteChoiceRevision = 1,
        },
        profile
      )
    );
    Assert.False(
      PlanningWorkPolicy.IsCompleted(
        saved,
        load with
        {
          ExecutionLegId = Guid.NewGuid(),
        },
        profile
      )
    );
    profile.HeightFeet++;
    Assert.False(PlanningWorkPolicy.IsCompleted(saved, load, profile));
  }
}
