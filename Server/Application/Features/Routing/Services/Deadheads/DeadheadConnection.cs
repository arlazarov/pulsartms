using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Deadheads;

public sealed record DeadheadConnection(
  RouteWorkSnapshot Previous,
  RouteWorkSnapshot Current,
  RouteWorkStop From,
  RouteWorkStop To
)
{
  public TruckRoute? ReadRoute(
    DispatchDeadhead? saved,
    TruckRouteProfile profile
  )
  {
    var route =
      saved is { RouteJson: { } json }
      && saved.PreviousDispatchId == Previous.Id
      && saved.PreviousExecutionLegId == Previous.ExecutionLegId
      && saved.InputHash == Signature(profile)
        ? SavedRouteReader.Route(json, 1)
        : null;
    if (
      route is null
      || From.Latitude is null
      || From.Longitude is null
      || To.Latitude is null
      || To.Longitude is null
    )
      return null;
    return RouteAnchoring.Matches(
      route,
      [
        new((double)From.Latitude, (double)From.Longitude),
        new((double)To.Latitude, (double)To.Longitude),
      ]
    )
      ? route
      : null;
  }

  public static DeadheadConnection? Find(DeadheadHistorySnapshot? snapshot) =>
    snapshot is null || snapshot.HasUnknownStart
      ? null
      : Find(snapshot.Current, snapshot.Predecessors);

  public static DeadheadConnection? Find(
    Load current,
    IEnumerable<Load> history
  ) =>
    Find(
      RouteWorkProjection.Capture(current.TruckItinerary()),
      history.Select(x => RouteWorkProjection.Capture(x.TruckItinerary()))
    );

  public static DeadheadConnection? Find(
    RouteWorkSnapshot current,
    IEnumerable<RouteWorkSnapshot> history
  )
  {
    current = RouteWorkProjection.TruckItinerary(current);
    history = history.Select(RouteWorkProjection.TruckItinerary);
    if (
      current.TruckId is not { } truck
      || current.Stops.Any(s => s.TruckId.HasValue && s.TruckId != truck)
    )
      return null;
    var pickup = current.Stops.OrderBy(s => s.Sequence).FirstOrDefault();
    var start = Start(current);
    if (pickup is null || !SourceWords.IsPickup(pickup.Job) || start is null)
      return null;
    var assigned = history
      .Where(x =>
        x.Id != current.Id
        && x.TruckId == truck
        && !SourceWords.IsCancelled(x.Status)
      )
      .Select(x => new { Load = x, Start = Start(x) })
      .ToArray();
    var deliveryOwners = assigned
      .Where(x =>
        x.Load.ExecutionStatus is "active" or "completed"
        && !x.Load.Stops.Any(stop => stop.AwaitingHandoff)
        && x.Load.Stops.OrderBy(stop => stop.Sequence).LastOrDefault()
          is { } last
        && IsDelivery(last)
      )
      .Select(x => x.Load.Id)
      .ToHashSet();
    assigned = assigned
      .Where(x =>
        x.Load.ExecutionStatus != "planned"
        || !deliveryOwners.Contains(x.Load.Id)
      )
      .ToArray();
    if (
      current.Status is "assigned" or "in_transit"
      && assigned.Count(x => x.Load.ExecutionStatus == "active") == 1
      && assigned.Any(x =>
        x.Load.ExecutionStatus == "active" && deliveryOwners.Contains(x.Load.Id)
      )
    )
      assigned = assigned
        .Where(x =>
          x.Load.ExecutionStatus is "active" or "planned"
          || x.Load.ExecutionLegId is null
            && x.Load.Status is "assigned" or "in_transit"
            && x.Load.Stops.OrderBy(stop => stop.Sequence)
              .LastOrDefault()
              ?.IsCompleted != true
        )
        .ToArray();
    if (assigned.Any(x => x.Start is null))
      return null;
    var candidates = assigned
      .Where(x => x.Start <= start)
      .OrderByDescending(x => x.Start)
      .Take(2)
      .ToArray();
    if (
      candidates.Length == 0
      || candidates[0].Start == start
      || candidates.Length > 1 && candidates[0].Start == candidates[1].Start
    )
      return null;
    var previous = candidates[0].Load;
    if (previous.Stops.Any(s => s.TruckId.HasValue && s.TruckId != truck))
      return null;
    var delivery = previous.Stops.OrderBy(s => s.Sequence).LastOrDefault();
    if (
      delivery is null
      || !IsDelivery(delivery)
      || At(delivery, previous.DeliveryDate) is null
    )
      return null;
    // Pickup order identifies the connection; overlapping appointments affect
    // ETA, not its road mileage.
    return new(previous, current, delivery, pickup);
  }

  private static DateTime? Start(RouteWorkSnapshot load) =>
    At(load.Stops.OrderBy(s => s.Sequence).FirstOrDefault(), load.ShipDate);

  private static DateTime? At(RouteWorkStop? stop, DateOnly? fallback) =>
    (stop?.ScheduledDate ?? fallback)?.ToDateTime(
      stop?.ScheduledTime ?? TimeOnly.MinValue
    );

  private static bool IsDelivery(RouteWorkStop stop) =>
    (stop.ManualAction ?? stop.Job) is { } action
    && (
      action.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
      || action.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
    );

  public string Signature(TruckRouteProfile profile)
  {
    var original = LegacySignature(profile);
    return !Previous.ExecutionLegId.HasValue && !Current.ExecutionLegId.HasValue
      ? original
      : Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            new
            {
              original,
              Previous.ExecutionLegId,
              Previous.AssignmentRevision,
              CurrentExecutionLegId = Current.ExecutionLegId,
              CurrentAssignmentRevision = Current.AssignmentRevision,
            }
          )
        )
      );
  }

  private string LegacySignature(TruckRouteProfile profile) =>
    Convert.ToHexString(
      SHA256.HashData(
        Encoding.UTF8.GetBytes(
          JsonSerializer.Serialize(
            new
            {
              Policy = "planned-deadhead-street-v2",
              Previous.Id,
              CurrentId = Current.Id,
              Current.TruckId,
              profile.HeightFeet,
              profile.WidthFeet,
              profile.LengthFeet,
              profile.WeightPounds,
              profile.Axles,
              profile.AxleWeightPounds,
              profile.Hazmat,
              Stops = new[] { From, To }.Select(s => new
              {
                s.Id,
                s.Address,
                s.City,
                s.Province,
                s.Country,
                s.ZipCode,
                s.Latitude,
                s.Longitude,
                ReliablePoint = StopLocation.ReliablePoint(s, DateTime.UtcNow)
                  is not null,
              }),
            }
          )
        )
      )
    );
}
