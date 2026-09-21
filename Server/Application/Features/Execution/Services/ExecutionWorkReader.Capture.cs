using System.Collections.Immutable;
using Application.Features.Execution.Models;
using Application.Reference;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Services;

// Turning a source load into the shape planning reads it in, and giving it
// the key the board sorts by: what the truck is doing now, then what it
// starts next, then what is merely booked.
public static partial class ExecutionWorkReader
{
  private sealed class SelectionRow
  {
    public required string Key { get; init; }
    public Guid? TruckId { get; init; }
    public string TruckNumber { get; init; } = "";
    public string DriverName { get; set; } = "";
    public string TrailerNumber { get; set; } = "";
    public List<WorkLoadReference> Loads { get; } = [];
  }

  private static ExecutionLoadSnapshot CaptureSource(Load source)
  {
    var snapshot = ExecutionLoadProjection.Capture(source);
    var work = snapshot.Work;
    var start = work.PlanningFromStopId.HasValue
      ? work.Stops.SingleOrDefault(s => s.Id == work.PlanningFromStopId)
      : work.Stops.FirstOrDefault(s =>
        s.TruckId.HasValue
        || !string.IsNullOrWhiteSpace(s.TruckNumber)
        || s.ManualStateAfter is not null and not "No truck"
      );
    var stops = StopOperation
      .Resolve(
        work.Stops,
        work.PlanningFromStopId,
        (stop, job, state) => stop with { Job = job, StateAfter = state }
      )
      .Select(stop =>
        start is not null && stop.Sequence < start.Sequence
        || stop.StateAfter == "No truck"
          ? stop with
          {
            Job = stop.ManualAction is null ? "Driver start" : stop.Job,
            StateAfter = "No truck",
          }
          : stop
      )
      .ToImmutableArray();
    return snapshot with
    {
      Work = work with
      {
        TruckNumber = string.IsNullOrWhiteSpace(work.TruckNumber)
          ? start?.TruckNumber ?? ""
          : work.TruckNumber,
        Stops = stops,
      },
    };
  }

  private static WorkLoadReference Reference(ExecutionLoadSnapshot snapshot)
  {
    var load = snapshot.Work;
    var details = snapshot.Details;
    return new(
      load.Id,
      load.ExecutionLegId,
      load.AssignmentRevision,
      load.ExecutionStatus,
      load.LoadNumber,
      details.OrderNumber,
      details.CustomerName,
      details.DriverName,
      load.DriverId,
      Order(load),
      load.Stops.Select(stop => new WorkVisitReference(
          stop.Id,
          stop.City,
          stop.Name
        ))
        .ToImmutableArray()
    );
  }

  private static WorkOrderKey Order(RouteWorkSnapshot load)
  {
    var first = load.Stops.FirstOrDefault(stop =>
      stop.StateAfter != "No truck"
    );
    var start = (
      first?.ScheduledDate ?? load.ShipDate ?? DateOnly.MaxValue
    ).ToDateTime(first?.ScheduledTime ?? TimeOnly.MinValue);
    return new(
      load.ExecutionStatus == "active" ? WorkActivity.ActiveExecution
        : ExecutionWorkRelevance.HasStarted(load) ? WorkActivity.Started
        : WorkActivity.Upcoming,
      start,
      load.LoadNumber
    );
  }
}
