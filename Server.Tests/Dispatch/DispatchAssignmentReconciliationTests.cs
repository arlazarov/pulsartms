using Application.Features.Dispatch.Commands.SyncDispatche;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class DispatchAssignmentReconciliationTests
{
  [Fact]
  public void ReassignmentReleasesOnlyTheFormerTruckConfirmation()
  {
    var (load, trucks) = SwappedLoad();
    var completion = DateTime.UtcNow.AddDays(-1);
    var recorded = DateTime.UtcNow;
    load.Stops[0].ManualCompletedAt = completion;
    load.Stops[0].ManualCompletionRevision = 2;

    Assert.True(
      DispatchAssignmentReconciliation.ReleaseStaleConfirmation(
        load,
        trucks,
        recorded
      )
    );

    Assert.Null(load.PlanningTruckId);
    Assert.Null(load.PlanningFromStopId);
    Assert.Equal(3, load.PlanningAssignmentRevision);
    Assert.Equal(recorded, load.PlanningAssignmentRecordedAt);
    Assert.Null(load.PlanningAssignmentRecordedBy);
    Assert.Equal(completion, load.Stops[0].ManualCompletedAt);
    Assert.Equal(2, load.Stops[0].ManualCompletionRevision);
    Assert.Equal(load.TruckId, load.TruckItinerary().TruckId);
    Assert.NotEmpty(load.TruckItinerary().Stops);
    Assert.False(
      DispatchAssignmentReconciliation.ReleaseStaleConfirmation(
        load,
        trucks,
        recorded
      )
    );
  }

  [Theory]
  [InlineData("same-truck")]
  [InlineData("missing-anchor")]
  [InlineData("manual-operation")]
  [InlineData("mixed-trucks")]
  [InlineData("missing-truck")]
  [InlineData("unresolved-number")]
  [InlineData("inactive-truck")]
  [InlineData("completed")]
  [InlineData("revision-limit")]
  public void AmbiguousOrUnchangedAssignmentsPreserveConfirmation(string mode)
  {
    var (load, trucks) = SwappedLoad();
    switch (mode)
    {
      case "same-truck":
        load.PlanningTruckId = load.TruckId;
        break;
      case "missing-anchor":
        load.PlanningFromStopId = Guid.NewGuid();
        break;
      case "manual-operation":
        load.Stops[0].ManualAction = "pickup";
        break;
      case "mixed-trucks":
        load.Stops[0].TruckId = load.PlanningTruckId;
        break;
      case "missing-truck":
        load.Stops[0].TruckId = null;
        break;
      case "unresolved-number":
        load.Stops[0].TruckNumber = "unknown";
        break;
      case "inactive-truck":
        trucks["11007"].IsActive = false;
        break;
      case "completed":
        load.Status = "completed";
        break;
      case "revision-limit":
        load.PlanningAssignmentRevision = long.MaxValue;
        break;
    }
    var truck = load.PlanningTruckId;
    var anchor = load.PlanningFromStopId;
    var revision = load.PlanningAssignmentRevision;

    Assert.False(
      DispatchAssignmentReconciliation.ReleaseStaleConfirmation(
        load,
        trucks,
        DateTime.UtcNow
      )
    );

    Assert.Equal(truck, load.PlanningTruckId);
    Assert.Equal(anchor, load.PlanningFromStopId);
    Assert.Equal(revision, load.PlanningAssignmentRevision);
  }

  private static (
    DispatchEntity Load,
    Dictionary<string, Truck> Trucks
  ) SwappedLoad()
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "11007",
      IsActive = true,
    };
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Sequence = 1,
      Job = "pickup",
      TruckId = truck.Id,
      TruckNumber = truck.UnitNumber,
    };
    var load = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      Status = "in_transit",
      TruckId = truck.Id,
      TruckNumber = truck.UnitNumber,
      PlanningTruckId = Guid.NewGuid(),
      PlanningFromStopId = stop.Id,
      PlanningAssignmentRevision = 2,
      PlanningAssignmentRecordedBy = Guid.NewGuid(),
      Stops = [stop],
    };
    return (load, new() { [truck.UnitNumber] = truck });
  }
}
