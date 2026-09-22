using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class PlanningCurrencyMetadataTests
{
  [Theory]
  [InlineData(false, false, false)]
  [InlineData(false, true, false)]
  [InlineData(false, true, true)]
  [InlineData(true, false, false)]
  [InlineData(true, true, false)]
  [InlineData(true, true, true)]
  [InlineData(true, true, false, "revision")]
  [InlineData(false, true, false, "truck")]
  [InlineData(true, true, false, "dispatch")]
  [InlineData(true, true, false, "leg")]
  [InlineData(false, true, false, "missing")]
  public async Task CurrencyUsesMetadataWithoutDecodingGeometry(
    bool native,
    bool completed,
    bool changedProfile,
    string? mismatch = null
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    if (native)
      await f.AddExecutionAsync("planned");
    var work = (
      await f.Planning.Itineraries.ReadAsync(
        f.Truck.Id,
        DateTimeOffset.UtcNow,
        default
      )
    )!;
    var candidate = PlanningWorkPolicy.Candidates(work).Single();
    var load = PlanningWorkPolicy.Resolve(work, candidate);
    var profile = await f.Planning.Routes.ProfileAsync(f.Truck.Id, default);
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      TruckId = f.Truck.Id,
      ExecutionLegId = load.ExecutionLegId,
      AssignmentRevision = load.AssignmentRevision,
      Tracking = new() { AllStopsPassed = completed },
    };
    switch (mismatch)
    {
      case "revision":
        plan.AssignmentRevision++;
        break;
      case "truck":
        plan.TruckId = Guid.NewGuid();
        break;
      case "dispatch":
        plan.DispatchId = Guid.NewGuid();
        break;
      case "leg":
        plan.ExecutionLegId = Guid.NewGuid();
        break;
    }
    if (mismatch != "missing")
      f.Db.DispatchRoutePlans.Add(
        new DispatchRoutePlan
        {
          Id = plan.Id,
          DispatchId = load.Id,
          TruckId = f.Truck.Id,
          ExecutionLegId = load.ExecutionLegId,
          AssignmentRevision = load.AssignmentRevision,
          InputHash = RoutePlanInputs.Hash(load, profile),
          PlanJson = JsonSerializer.Serialize(plan, RoutingJson.Options),
          // Invalid geometry is deliberately unreadable by the full-plan path.
          GeometryManifestJson = "not a geometry manifest",
        }
      );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    if (changedProfile)
      profile.HeightFeet += 1;
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    Assert.Equal(
      !completed || changedProfile || mismatch is not null,
      await PlanningCurrency.IsCurrentAsync(work, load, store, profile, default)
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task MetadataSharesRouteInvalidation(bool native)
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var legId = native
      ? (await f.AddExecutionAsync("planned")).Id
      : (Guid?)null;
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = f.Load.Id,
      TruckId = f.Truck.Id,
      ExecutionLegId = legId,
    };
    f.Db.DispatchRoutePlans.Add(
      new DispatchRoutePlan
      {
        Id = plan.Id,
        DispatchId = f.Load.Id,
        TruckId = f.Truck.Id,
        ExecutionLegId = legId,
        PlanJson = JsonSerializer.Serialize(plan, RoutingJson.Options),
      }
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    var store = new RoutePlanStore(
      f.Db,
      f.Planning.Reads,
      f.Planning.Profiles,
      new SavedRoutePlanReader(f.Db, NullLogger<SavedRoutePlanReader>.Instance)
    );
    var first = (await store.ReadMetadataAsync(f.Load.Id, default, legId))!;
    first.Tracking.AllStopsPassed = true;
    await f.Db.DispatchRoutePlans.ExecuteDeleteAsync();
    var cached = await store.ReadMetadataAsync(f.Load.Id, default, legId);
    Assert.NotNull(cached);
    Assert.False(cached.Tracking.AllStopsPassed);
    f.Planning.Reads.Invalidate(RoutePlanStore.CacheKey(f.Load.Id, legId));
    Assert.Null(await store.ReadMetadataAsync(f.Load.Id, default, legId));
  }
}
