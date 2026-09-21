using Domain.Entities.Dispatch;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Models;

public static class SwitchPlanningRules
{
  public static bool Valid(PlanSwitchRequest? request) =>
    request?.Loads is { Count: >= 1 and <= 8 }
    && request.IdempotencyKey != Guid.Empty
    && !string.IsNullOrWhiteSpace(request.SiteName)
    && request.SiteName.Length <= 500
    && request.Latitude is >= -90 and <= 90
    && request.Longitude is >= -180 and <= 180
    && request.Loads.All(x =>
      x is not null
      && x.DispatchId != Guid.Empty
      && x.Outgoing is not null
      && x.Incoming is not null
      && x.Outgoing != x.Incoming
      && x.SourceSignature?.Length == 64
      && (
        !request.ConfirmCompleted
        || !x.OutgoingLegId.HasValue
          && x.ReleaseVisitId.HasValue
          && x.ReceiveVisitId.HasValue
      )
      && x.TransferKind is "drop_hook" or "resource_handoff"
      && (
        x.TransferKind != "drop_hook"
        || x.Outgoing.TrailerId.HasValue
          && x.Outgoing.TrailerId == x.Incoming.TrailerId
      )
      && (
        x.TransferKind != "resource_handoff"
        || x.Outgoing.TrailerId == x.Incoming.TrailerId
          && (
            !x.Outgoing.TrailerId.HasValue
            || x.Outgoing.TruckId == x.Incoming.TruckId
          )
      )
      && (
        !(x.PlannedReleaseAt ?? request.PlannedAt).HasValue
        || !(x.PlannedReceiveAt ?? request.PlannedAt).HasValue
        || (x.PlannedReceiveAt ?? request.PlannedAt)
          >= (x.PlannedReleaseAt ?? request.PlannedAt)
      )
    )
    && request.Loads.Select(x => x.DispatchId).Distinct().Count()
      == request.Loads.Count
    && request.Loads.Select(x => x.Outgoing.TruckId).Distinct().Count()
      == request.Loads.Count
    && Unique(
      request.Loads.SelectMany(x =>
        new[] { x.Outgoing.DriverId, x.Outgoing.CoDriverId }
      )
    )
    && Unique(request.Loads.Select(x => x.Outgoing.TrailerId))
    && (
      !request.ConfirmCompleted
      || request.Loads.Select(x => x.Incoming.TruckId).Distinct().Count()
        == request.Loads.Count
        && Unique(request.Loads.Select(x => x.Incoming.TrailerId))
        && Unique(
          request.Loads.SelectMany(x =>
            new[] { x.Incoming.DriverId, x.Incoming.CoDriverId }
          )
        )
    );

  private static bool Unique(IEnumerable<Guid?> ids)
  {
    var values = ids.Where(x => x.HasValue).ToArray();
    return values.Distinct().Count() == values.Length;
  }

  public static bool BootstrapMatches(
    DispatchEntity load,
    SwitchLoadChange item,
    IReadOnlyCollection<DispatchStop> before
  )
  {
    var ids = before.Select(x => x.Id).ToHashSet();
    return !load.Stops.Any(x =>
      ids.Contains(x.Id)
      && x.IsCompleted
      && (
        x.TruckId.HasValue && x.TruckId != item.Outgoing.TruckId
        || x.DriverId.HasValue && x.DriverId != item.Outgoing.DriverId
        || x.CoDriverId.HasValue && x.CoDriverId != item.Outgoing.CoDriverId
        || x.TrailerId.HasValue && x.TrailerId != item.Outgoing.TrailerId
      )
    );
  }

  public static (List<DispatchStop> Before, List<DispatchStop> After)? Split(
    DispatchEntity load,
    SwitchLoadChange item,
    bool confirmCompleted = false
  )
  {
    if (
      load.Status is "completed" or "cancelled" or "canceled"
      || !item.OutgoingLegId.HasValue
        && (load.PlanningTruckId.HasValue || load.PlanningFromStopId.HasValue)
      || ExecutionSnapshots.Fingerprint(load) != item.SourceSignature
    )
      return null;
    var stops = StopOperation.Resolve(load.Stops, load.PlanningFromStopId);
    List<DispatchStop> before;
    List<DispatchStop> after;
    if (item.ReleaseVisitId.HasValue || item.ReceiveVisitId.HasValue)
    {
      var at = stops.FindIndex(x => x.Id == item.ReleaseVisitId);
      if (
        at < 1
        || at + 2 >= stops.Count
        || stops[at + 1].Id != item.ReceiveVisitId
        || !confirmCompleted
          && (stops[at].IsCompleted || stops[at + 1].IsCompleted)
      )
        return null;
      before = stops.Take(at).Select(ExecutionSnapshots.Copy).ToList();
      after = stops.Skip(at + 2).Select(ExecutionSnapshots.Copy).ToList();
    }
    else
    {
      var at = stops.FindIndex(x => x.Id == item.SplitAfterVisitId);
      if (at < 0 || at + 1 >= stops.Count)
        return null;
      before = stops.Take(at + 1).Select(ExecutionSnapshots.Copy).ToList();
      after = stops.Skip(at + 1).Select(ExecutionSnapshots.Copy).ToList();
    }
    return after.Any(x => x.IsCompleted) ? null : (before, after);
  }
}
