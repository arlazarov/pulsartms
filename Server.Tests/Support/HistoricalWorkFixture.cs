using Application.Features.Routing.Services.Deadheads;
using Domain.Models.Execution;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Support;

internal static class HistoricalWorkFixture
{
  public static Load Copy(Load source) =>
    new()
    {
      Id = source.Id,
      TruckId = source.TruckId,
      TruckNumber = source.TruckNumber,
      Status = source.Status,
      ExecutionLegId = source.ExecutionLegId,
      ExecutionStatus = source.ExecutionStatus,
      AssignmentRevision = source.AssignmentRevision,
      PlanningTruckId = source.PlanningTruckId,
      PlanningFromStopId = source.PlanningFromStopId,
      PlanningAssignmentRevision = source.PlanningAssignmentRevision,
      RouteChoiceRevision = source.RouteChoiceRevision,
      ShipDate = source.ShipDate,
      DeliveryDate = source.DeliveryDate,
      Price = source.Price,
      Currency = source.Currency,
      LoadedMiles = source.LoadedMiles,
      Stops = source.Stops.Select(ExecutionSnapshots.Copy).ToList(),
    };

  public static async Task<Load> AddAsync(AppDbContext db, Load source)
  {
    var load = Copy(source);
    load.Id = Guid.NewGuid();
    load.LoadNumber = await db.Dispatches.MaxAsync(x => x.LoadNumber) + 1;
    load.Status = "completed";
    load.ExecutionLegId = null;
    load.ExecutionStatus = null;
    load.ShipDate = source.ShipDate?.AddDays(-1);
    load.DeliveryDate = source.DeliveryDate?.AddDays(-1);
    foreach (var stop in load.Stops)
    {
      stop.Id = Guid.NewGuid();
      stop.DispatchId = load.Id;
      stop.ScheduledDate = stop.ScheduledDate?.AddDays(-1);
    }
    db.Dispatches.Add(load);
    await db.SaveChangesAsync();
    return load;
  }

  public static async Task ChangeAsync(
    AppDbContext db,
    Load history,
    string change
  )
  {
    switch (change)
    {
      case "endpoint":
        history.Stops[^1].Longitude += 1;
        break;
      case "completion":
        history.Stops[^1].ManualCompletionRevision++;
        break;
      case "unknown":
        history.ShipDate = null;
        history.Stops[0].ScheduledDate = null;
        break;
      case "cancel":
        history.Status = "cancelled";
        break;
      case "price":
        history.Price = (history.Price ?? 0) + 50;
        break;
      case "insert":
        var inserted = await AddAsync(db, history);
        inserted.ShipDate = null;
        inserted.Stops[0].ScheduledDate = null;
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(change));
    }
    await db.SaveChangesAsync();
  }
}
