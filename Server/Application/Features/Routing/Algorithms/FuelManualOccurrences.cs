using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;

namespace Application.Features.Routing.Algorithms;

public static class FuelManualOccurrences
{
  public static List<FuelCandidate> Resolve(
    FuelHorizonResult horizon,
    IReadOnlyList<PricedFuelStation> prices,
    IReadOnlyList<FuelPlanEditStop> edits,
    FuelSearchGeometry geometry,
    CancellationToken ct,
    IReadOnlyDictionary<Guid, string>? stationNames = null
  )
  {
    var wanted = edits.Select(x => x.StationId).ToHashSet();
    var available = new Dictionary<Guid, List<FuelCandidate>>();
    foreach (
      var price in prices
        .Where(x => wanted.Contains(x.Station.StationId))
        .GroupBy(x => x.Station.StationId)
        .Select(group => group.MinBy(x => x.EconomicUsd)!)
    )
    {
      var occurrences = new List<FuelCandidate>();
      double offset = 0;
      for (var index = 0; index < horizon.Route.Legs.Count; index++)
      {
        ct.ThrowIfCancellationRequested();
        var match = geometry.MatchLeg(index, price.Station.Point, ct);
        if (match.Away <= FuelAccessEstimate.NearbyMiles)
        {
          var candidate = FuelAccessEstimate
            .Nearby(
              [
                new(
                  price.Station,
                  offset + match.Along,
                  match.Away,
                  0,
                  price.CashUsd,
                  price.EconomicUsd
                )
                {
                  LegIndex = index,
                },
              ]
            )
            .Single();
          candidate.Station.BeforeStopId = horizon.Itinerary[index].Stop.Id;
          candidate.Station.DispatchId = horizon.Itinerary[index].DispatchId;
          occurrences.Add(candidate);
        }
        offset += horizon.Route.Legs[index].Miles;
      }
      available.Add(price.Station.StationId, occurrences);
    }
    var result = new List<FuelCandidate>();
    var used = new HashSet<string>(StringComparer.Ordinal);
    double previous = -1;
    foreach (var edit in edits)
    {
      ct.ThrowIfCancellationRequested();
      var label =
        $"Fuel stop {result.Count + 1} — {StationName(edit.StationId)}";
      if (!available.TryGetValue(edit.StationId, out var occurrences))
        throw new RoutePlanningException(
          $"{label}: no current price is available. Choose another station or remove this stop."
        );
      if (occurrences.Count == 0)
        throw new RoutePlanningException(
          $"{label} is not within {FuelAccessEstimate.NearbyMiles} miles of the remaining route. Choose a station closer to the route or remove this stop."
        );
      var matching = occurrences
        .Where(x =>
          !edit.BeforeStopId.HasValue
          || x.Station.BeforeStopId == edit.BeforeStopId
        )
        .ToList();
      if (matching.Count == 0)
        throw new RoutePlanningException(
          $"{label} is not near this part of the trip. Choose a different pickup/delivery position or remove this stop."
        );
      var unused = matching.Where(x => !used.Contains(x.VisitKey)).ToList();
      if (unused.Count == 0)
        throw new RoutePlanningException(
          $"{label} is already included for this visit. Remove the duplicate stop."
        );
      var candidate = unused.FirstOrDefault(x => x.AlongMiles > previous + .01);
      if (candidate is null)
      {
        var precedingName =
          result[^1].Station.StationId == edit.StationId
            ? $"fuel stop {result.Count}"
            : StationName(result[^1].Station.StationId);
        if (unused.Any(x => Math.Abs(x.AlongMiles - previous) <= .01))
          throw new RoutePlanningException(
            $"{label} shares the same route position as {precedingName}. Keep only one of these stops."
          );
        throw new RoutePlanningException(
          $"{label} comes before {precedingName} on the route. Move it earlier in the plan or remove it."
        );
      }
      result.Add(candidate);
      used.Add(candidate.VisitKey);
      previous = candidate.AlongMiles;
    }
    return result;

    string StationName(Guid stationId)
    {
      var name = prices
        .FirstOrDefault(x => x.Station.StationId == stationId)
        ?.Station.Name;
      if (string.IsNullOrWhiteSpace(name))
        name = stationNames?.GetValueOrDefault(stationId);
      return string.IsNullOrWhiteSpace(name)
        ? "Unavailable station"
        : name.Trim();
    }
  }
}
