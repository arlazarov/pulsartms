using System.Text.Json;
using Application.Features.Dispatch.Models;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchEnrichmentTests
{
  [Fact]
  public void SupplementalPayloadRetainsVersionsWithoutRepeatingStopAddressesAndCargo()
  {
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      AssignmentRevision = 17,
      LastSyncedAt = DateTime.UtcNow,
      PlanningAssignmentRevision = 12,
      RouteChoiceRevision = 3,
      LoadedMiles = 100,
      EmptyMiles = 20,
      LoadedRatePerMile = 2,
      TotalRatePerMile = 1.67m,
      CustomerName = new('x', 200),
      Stops = Enumerable
        .Range(1, 12)
        .Select(i => new DispatchStopResponse
        {
          Id = Guid.NewGuid(),
          Sequence = i,
          Address = new('x', 200),
          Name = new('y', 100),
          ManualCompletionRevision = i,
          OperationRevision = i + 1,
        })
        .ToList(),
    };
    var row = new TruckDispatchBoardResponse
    {
      Key = "row",
      TruckId = load.TruckId,
      DriverName = "Driver",
      Dispatches = [load],
    };
    var compact = TruckDispatchEnrichment.From(row, true);
    var item = Assert.Single(compact.Dispatches);
    Assert.Equal(load.Id, item.Id);
    Assert.Equal(load.LastSyncedAt, item.LastSyncedAt);
    Assert.Equal(load.ExecutionLegId, item.ExecutionLegId);
    Assert.Equal(load.AssignmentRevision, item.AssignmentRevision);
    Assert.Equal(12, item.PlanningAssignmentRevision);
    Assert.Equal(3, item.RouteChoiceRevision);
    Assert.Equal(120m, item.Financials!.TotalMiles);
    Assert.Equal(load.Stops.Select(x => x.Id), item.Stops.Select(x => x.Id));
    Assert.Equal(
      load.Stops.Select(x => x.OperationRevision),
      item.Stops.Select(x => x.OperationRevision)
    );
    Assert.Null(item.Eta);
    Assert.Null(
      Assert
        .Single(TruckDispatchEnrichment.From(row, false).Dispatches)
        .Financials
    );
    var json = JsonSerializer.Serialize(compact);
    Assert.DoesNotContain("Address", json);
    Assert.DoesNotContain("CustomerName", json);
    Assert.True(json.Length < JsonSerializer.Serialize(row).Length / 3);
  }
}
