using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Models;

public sealed record ExecutionSourceTopology(
  IReadOnlyDictionary<Guid, List<DispatchStop>> Stops,
  IReadOnlySet<Guid> ChangedLegIds,
  bool NeedsReview
)
{
  public static ExecutionSourceTopology Resolve(
    DispatchEntity source,
    IReadOnlyList<ExecutionLeg> legs,
    IReadOnlyDictionary<Guid, List<DispatchStop>> snapshots,
    IReadOnlyList<ExecutionTransferVisit> nativeVisits
  )
  {
    var native = nativeVisits.ToDictionary(x => x.Id);
    var mapped = nativeVisits
      .Where(x => x.SourceDispatchStopId.HasValue)
      .Select(x => x.SourceDispatchStopId!.Value)
      .ToHashSet();
    var supplied = StopOperation.Resolve(
      source.Stops,
      source.PlanningFromStopId
    );
    var order = supplied.Select(x => x.Id).ToArray();
    var saved = legs.SelectMany(x => snapshots[x.Id]).ToArray();
    var savedOrder = saved
      .Where(x => !native.ContainsKey(x.Id))
      .Select(x => x.Id)
      .ToArray();
    var known = savedOrder.ToHashSet();
    var sourceOrder = order.Where(x => !mapped.Contains(x)).ToArray();
    if (savedOrder.SequenceEqual(sourceOrder))
      return new(snapshots, new HashSet<Guid>(), false);
    if (
      savedOrder.Length != known.Count
      || !savedOrder.SequenceEqual(sourceOrder.Where(known.Contains))
      || !known.IsSubsetOf(sourceOrder)
      || order.Distinct().Count() != order.Length
    )
      return Review();

    var positions = supplied
      .Select((stop, index) => (stop.Id, index))
      .ToDictionary(x => x.Id, x => x.index);
    var anchors = legs.SelectMany(leg =>
        snapshots[leg.Id]
          .Select(stop => new { Leg = leg, Position = Position(stop) })
      )
      .Where(x => x.Position.HasValue)
      .OrderBy(x => x.Position)
      .ToArray();
    var updates = new Dictionary<Guid, List<DispatchStop>>(snapshots);
    var changed = new HashSet<Guid>();
    foreach (
      var stop in supplied.Where(x =>
        !known.Contains(x.Id) && !mapped.Contains(x.Id)
      )
    )
    {
      var at = positions[stop.Id];
      var before = anchors.LastOrDefault(x => x.Position < at);
      var after = anchors.FirstOrDefault(x => x.Position > at);
      var owner = before?.Leg ?? after?.Leg;
      if (
        owner is null
        || before is not null && after is not null && before.Leg != after.Leg
        || before is null && (owner != legs[0] || owner.StartSwitchId.HasValue)
        || after is null && (owner != legs[^1] || owner.EndSwitchId.HasValue)
        || owner.Status is not ("active" or "planned")
        || owner.Loads.Count != 1
        || ConflictingAssignment(stop, owner)
        || !Ordinary(stop)
        || HasActual(stop)
      )
        return Review();
      if (changed.Add(owner.Id))
        updates[owner.Id] = snapshots[owner.Id]
          .Select(ExecutionSnapshots.Copy)
          .ToList();
      var target = updates[owner.Id];
      var insert = target.FindIndex(x => Position(x) > at);
      if (insert < 0)
        insert = owner.EndSwitchId.HasValue ? target.Count - 1 : target.Count;
      if (
        insert < 0
        || owner.StartSwitchId.HasValue && insert == 0
        || target.Skip(insert).Any(HasActual)
        || target.Count >= 49
      )
        return Review();
      var added = ExecutionSnapshots.Copy(stop);
      added.TruckId = owner.TruckId;
      added.DriverId = owner.DriverId;
      added.CoDriverId = owner.CoDriverId;
      added.TrailerId = owner.TrailerId;
      target.Insert(insert, added);
    }
    foreach (var id in changed)
    {
      var stops = updates[id];
      for (var i = 0; i < stops.Count; i++)
        stops[i].Sequence = i + 1;
    }
    return new(updates, changed, false);

    int? Position(DispatchStop stop)
    {
      var id = native.TryGetValue(stop.Id, out var visit)
        ? visit.SourceDispatchStopId
        : stop.Id;
      return id.HasValue && positions.TryGetValue(id.Value, out var value)
        ? value
        : null;
    }

    ExecutionSourceTopology Review() =>
      new(snapshots, new HashSet<Guid>(), true);
  }

  private static bool Ordinary(DispatchStop stop) =>
    stop.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
    || stop.Job.Equals("Pickup", StringComparison.OrdinalIgnoreCase)
    || stop.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
    || stop.Job.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
    || stop.Job.Equals("Waypoint", StringComparison.OrdinalIgnoreCase);

  private static bool HasActual(DispatchStop stop) =>
    stop.IsCompleted
    || stop.ManualCompletedAt.HasValue
    || stop.ArrivedAt.HasValue
    || stop.PickedUpAt.HasValue
    || stop.DeliveredAt.HasValue
    || stop.DepartedAt.HasValue;

  private static bool ConflictingAssignment(
    DispatchStop stop,
    ExecutionLeg owner
  ) =>
    (
      stop.TruckId.HasValue
        ? stop.TruckId != owner.TruckId
        : !string.IsNullOrWhiteSpace(stop.TruckNumber)
    )
    || stop.DriverId.HasValue && stop.DriverId != owner.DriverId
    || stop.CoDriverId.HasValue && stop.CoDriverId != owner.CoDriverId
    || stop.TrailerId.HasValue && stop.TrailerId != owner.TrailerId;
}
