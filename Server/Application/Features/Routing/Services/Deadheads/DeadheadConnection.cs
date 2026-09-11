using Application.Features.Routing.Models;
using Application.Features.Routing.Algorithms;
using Domain.Entities.Dispatch;
using Application.Features.Routing.Services.Routes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Deadheads;

public sealed record DeadheadConnection(Load Previous, Load Current, DispatchStop From, DispatchStop To)
{
  public TruckRoute? ReadRoute(DispatchDeadhead? saved, TruckRouteProfile profile)
  {
    var route = saved is { RouteJson: { } json } && saved.PreviousDispatchId == Previous.Id && saved.InputHash == Signature(profile)
      ? SavedRouteReader.Route(json, 1) : null;
    if (route is null || From.Latitude is null || From.Longitude is null || To.Latitude is null || To.Longitude is null) return null;
    return RouteAnchoring.Matches(route, [new((double)From.Latitude, (double)From.Longitude),
      new((double)To.Latitude, (double)To.Longitude)]) ? route : null;
  }

  public static DeadheadConnection? Find(DeadheadHistorySnapshot? snapshot) =>
    snapshot is null || snapshot.HasUnknownStart ? null : Find(snapshot.Current, snapshot.Predecessors);

  public static DeadheadConnection? Find(Load current, IEnumerable<Load> history)
  {
    if (current.TruckId is not { } truck || current.Stops.Any(s => s.TruckId.HasValue && s.TruckId != truck)) return null;
    var pickup = current.Stops.OrderBy(s => s.Sequence).FirstOrDefault();
    var start = Start(current);
    if (pickup is null || !string.Equals(pickup.Job, "Pick Up", StringComparison.OrdinalIgnoreCase) || start is null) return null;
    var assigned = history.Where(x => x.Id != current.Id && x.TruckId == truck && x.Status != "cancelled")
      .Select(x => new { Load = x, Start = Start(x) }).ToArray();
    if (assigned.Any(x => x.Start is null)) return null;
    var candidates = assigned.Where(x => x.Start <= start)
      .OrderByDescending(x => x.Start).Take(2).ToArray();
    if (candidates.Length == 0 || candidates[0].Start == start
      || candidates.Length > 1 && candidates[0].Start == candidates[1].Start) return null;
    var previous = candidates[0].Load;
    if (previous.Stops.Any(s => s.TruckId.HasValue && s.TruckId != truck)) return null;
    var delivery = previous.Stops.OrderBy(s => s.Sequence).LastOrDefault();
    if (delivery is null || !string.Equals(delivery.Job, "Drop Off", StringComparison.OrdinalIgnoreCase)
      || At(delivery, previous.DeliveryDate) is null) return null;
    // Pickup order identifies the connection; overlapping appointments affect ETA, not its road mileage.
    return new(previous, current, delivery, pickup);
  }

  private static DateTime? Start(Load load) => At(load.Stops.OrderBy(s => s.Sequence).FirstOrDefault(), load.ShipDate);
  private static DateTime? At(DispatchStop? stop, DateOnly? fallback) =>
    (stop?.ScheduledDate ?? fallback)?.ToDateTime(stop?.ScheduledTime ?? TimeOnly.MinValue);

  public string Signature(TruckRouteProfile profile) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
    JsonSerializer.Serialize(new {
      Policy = "planned-deadhead-street-v1", Previous.Id, CurrentId = Current.Id, Current.TruckId,
      profile.HeightFeet, profile.WidthFeet, profile.LengthFeet, profile.WeightPounds, profile.Axles, profile.AxleWeightPounds, profile.Hazmat,
      Stops = new[] { From, To }.Select(s => new { s.Id, s.Address, s.City, s.Province, s.Country, s.ZipCode, s.Latitude, s.Longitude })
    }))));
}
