using Application.Features.Dispatch.Models;
using Domain.Entities.Execution;

namespace Application.Features.Dispatch.Services;

public sealed record DispatchCorrectionTarget(
  ExecutionLeg? Leg,
  Guid StopId,
  StopCorrectionRequest Request
)
{
  public IReadOnlySet<Guid>? DriverStops { get; init; }
  public IReadOnlySet<Guid>? CompletionStops { get; init; }
}

public static class DispatchCorrectionScope
{
  public static (List<DispatchCorrectionTarget> Targets, string? Error) Resolve(
    DispatchWorkspaceState state,
    Guid selectedId,
    StopCorrectionRequest request
  )
  {
    var rows = state.Response.Stops;
    var selected = rows.Single(x => x.Id == selectedId);
    var from = 0;
    var to = rows.Count - 1;
    if (request.ChangeAssignment)
    {
      if (
        request.ChangeTruck == false
        && request.ChangeTrailer == false
        && request.ChangeDriver == false
        && request.ChangeCoDriver == false
      )
        return ([], "Select at least one resource to change.");
      switch (request.AssignmentScope)
      {
        case "stop":
          from = to = rows.FindIndex(x => x.Id == selectedId);
          break;
        case "current":
          from = rows.FindIndex(x =>
            x.ExecutionLegId == selected.ExecutionLegId
          );
          to = rows.FindLastIndex(x =>
            x.ExecutionLegId == selected.ExecutionLegId
          );
          break;
        case "all":
          break;
        case "onward":
          from = rows.FindIndex(x => x.Id == selectedId);
          break;
        case "range":
          from = rows.FindIndex(x => x.Id == request.FromStopId);
          to = rows.FindIndex(x => x.Id == request.ToStopId);
          break;
        default:
          return ([], "Select an assignment scope.");
      }
      if (from < 0 || to < from)
        return ([], "Select a valid stop range from this load.");
    }
    var affected = request.ChangeAssignment
      ? rows.Skip(from).Take(to - from + 1).ToList()
      : [];
    var completed = state
      .Response.Load.Stops.Where(x => x.IsCompleted)
      .Select(x => x.Id)
      .ToHashSet();
    var completionRows =
      request.Completion == "completed"
        ? rows.Take(rows.FindIndex(x => x.Id == selectedId) + 1)
          .Where(x => x.Id == selectedId || !completed.Contains(x.Id))
          .ToList()
        : [];
    if (completionRows.Any(x => !x.CanCorrect || x.Transfer is not null))
      return ([], "Confirm earlier transfers before completing this stop.");
    var legIds = affected
      .Select(x => x.ExecutionLegId)
      .Concat(completionRows.Select(x => x.ExecutionLegId))
      .Append(selected.ExecutionLegId)
      .Distinct()
      .ToList();
    var result = new List<DispatchCorrectionTarget>();
    foreach (var id in legIds)
    {
      var legRows = rows.Where(x => x.ExecutionLegId == id).ToList();
      var changesAssignment = affected.Any(x => x.ExecutionLegId == id);
      var driversOnly =
        request.ChangeTruck == false
        && request.ChangeTrailer == false
        && (request.ChangeDriver == true || request.ChangeCoDriver == true);
      if (
        changesAssignment
        && !driversOnly
        && legRows.Any(x => !affected.Contains(x))
      )
        return (
          [],
          "This range cuts through an existing assignment. Select its full stop range; partial-leg corrections are not yet supported."
        );
      var leg = state.Legs.SingleOrDefault(x => x.Id == id);
      if (changesAssignment && leg is not null && leg.Loads.Count != 1)
        return (
          [],
          "This assignment is shared with another load and requires a coordinated change."
        );
      if (
        completionRows.Any(x => x.ExecutionLegId == id)
        && leg is { Status: "planned", StartSwitchId: not null }
      )
        return ([], "Confirm receipt before recording this assignment's work.");
      var reference = state.EffectiveStops[legRows[0].Id];
      if (changesAssignment && (leg is null || driversOnly))
      {
        foreach (var row in legRows.Where(affected.Contains))
        {
          var original = state.EffectiveStops[row.Id];
          var driver =
            request.ChangeDriver != false
              ? request.DriverId
              : original.DriverId;
          var coDriver =
            request.ChangeCoDriver != false
              ? request.CoDriverId
              : original.CoDriverId;
          if (driver.HasValue && driver == coDriver)
            return (
              [],
              $"Driver and co-driver must differ at stop {row.Sequence}."
            );
        }
      }
      var copy = new StopCorrectionRequest
      {
        Completion =
          id == selected.ExecutionLegId ? request.Completion
          : completionRows.Any(x => x.ExecutionLegId == id) ? "completed"
          : "keep",
        CompletedAt =
          id == selected.ExecutionLegId ? request.CompletedAt : null,
        ChangeAssignment = changesAssignment,
        ChangeTruck = request.ChangeTruck,
        ChangeTrailer = request.ChangeTrailer,
        ChangeDriver = request.ChangeDriver,
        ChangeCoDriver = request.ChangeCoDriver,
        TruckId =
          request.ChangeTruck != false
            ? request.TruckId
            : leg?.TruckId ?? reference.TruckId,
        TrailerId =
          request.ChangeTrailer != false ? request.TrailerId
          : leg is null ? reference.TrailerId
          : leg.TrailerId,
        DriverId =
          request.ChangeDriver != false ? request.DriverId
          : leg is null ? reference.DriverId
          : leg.DriverId,
        CoDriverId =
          request.ChangeCoDriver != false ? request.CoDriverId
          : leg is null ? reference.CoDriverId
          : leg.CoDriverId,
      };
      result.Add(
        new(
          leg,
          id == selected.ExecutionLegId ? selectedId : legRows[0].Id,
          copy
        )
        {
          CompletionStops =
            request.Completion == "completed"
              ? completionRows
                .Where(x => x.ExecutionLegId == id)
                .Select(x => x.Id)
                .ToHashSet()
              : null,
          DriverStops =
            changesAssignment && driversOnly
              ? affected
                .Where(x => x.ExecutionLegId == id)
                .Select(x => x.Id)
                .ToHashSet()
              : null,
        }
      );
    }
    return (result, null);
  }
}
