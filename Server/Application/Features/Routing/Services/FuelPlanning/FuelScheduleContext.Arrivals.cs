using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed partial class FuelScheduleContext
{
  public IReadOnlyDictionary<string, DateTimeOffset> Arrivals(
    TruckRoute route,
    IReadOnlyList<FuelCandidate> candidates,
    FuelSearchGeometry geometry,
    double initialAccessMiles,
    bool includeAccess,
    CancellationToken ct
  )
  {
    if (
      unavailable is not null
      || candidates.Count == 0
      || candidates.Count > 2_000
    )
      return new Dictionary<string, DateTimeOffset>();
    var legs = new List<RouteLeg>();
    var visits = new List<FuelItineraryStop>();
    var keys = new Dictionary<Guid, string>();
    double start = 0,
      total = 0,
      previousExit = initialAccessMiles;
    for (var index = 0; index < route.Legs.Count; index++)
    {
      var leg = route.Legs[index];
      var end = start + leg.Miles;
      var cursor = start;
      foreach (
        var candidate in candidates
          .Where(x => x.LegIndex == index)
          .OrderBy(x => x.AlongMiles)
      )
      {
        ct.ThrowIfCancellationRequested();
        var id = Guid.NewGuid();
        var mile = Math.Clamp(candidate.AlongMiles, cursor, end);
        var entry = includeAccess ? candidate.ExtraInMiles : 0;
        var stop = new PlanStop(
          id,
          candidate.Station.Name,
          candidate.Station.Address,
          visits.Count + 1,
          candidate.Station.Point
        )
        {
          Job = "Waypoint",
        };
        Add(stop, mile, entry);
        keys[id] = candidate.VisitKey;
        previousExit = includeAccess ? candidate.ExtraOutMiles : 0;
      }
      Add(itinerary[index].Stop, end, 0);
      start = end;

      void Add(PlanStop stop, double mile, double entry)
      {
        var miles = mile - cursor;
        var access = previousExit + entry;
        var seconds =
          (leg.Miles > 0 ? leg.Seconds * miles / leg.Miles : leg.Seconds)
          + FuelAccessEstimate.DrivingMinutes(access) * 60;
        var points = new List<RoutePoint>
        {
          visits.Count == 0 ? geometry.At(0, ct) : visits[^1].Stop.Point,
        };
        for (var at = cursor; at < mile; at += 20)
          points.Add(geometry.At(at, ct));
        points.Add(stop.Point);
        legs.Add(new(miles + access, seconds, points));
        total += miles + access;
        visits.Add(itinerary[index] with { Stop = stop, EndMiles = total });
        cursor = mile;
        previousExit = 0;
      }
    }
    var preview = new TruckRoute
    {
      Legs = legs,
      Miles = total,
      Seconds = legs.Sum(x => x.Seconds),
      CalculatedAt = route.CalculatedAt,
    };
    return Replay(preview, ct, visits)
      .Stops.Where(x => keys.ContainsKey(x.StopId))
      .ToDictionary(x => keys[x.StopId], x => x.Arrival);
  }
}
