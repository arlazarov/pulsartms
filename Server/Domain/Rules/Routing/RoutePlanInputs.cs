using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Domain.Rules.Routing;

// The fingerprint of everything a plan was built from: the truck, its
// dimensions, the stops in order and where they are. A saved plan whose
// fingerprint no longer matches was built for different work and is not
// reused. Every service that stores or reads a plan asks this, which is
// why it is not a part of any one of them.
public static class RoutePlanInputs
{
  public static string Hash(DispatchEntity load, TruckRouteProfile profile) =>
    Hash(RouteWorkProjection.Capture(load.TruckItinerary()), profile);

  public static string Hash(RouteWorkSnapshot load, TruckRouteProfile profile)
  {
    load = RouteWorkProjection.TruckItinerary(load);
    return load.ExecutionLegId is { } legId
        ? Convert.ToHexString(
          SHA256.HashData(
            Encoding.UTF8.GetBytes(
              $"{legId}:{load.AssignmentRevision}:{HashItinerary(load, profile)}"
            )
          )
        )
      : load.RouteChoiceRevision == 0 ? HashItinerary(load, profile)
      : Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{HashItinerary(load, profile)}:{load.RouteChoiceRevision}"
          )
        )
      );
  }

  private static string HashItinerary(
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  ) =>
    StopCompletionIdentity.Revise(
      Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
              new
              {
                LocationPolicy = "google-street-address-v1",
                load.TruckId,
                profile.HeightFeet,
                profile.WidthFeet,
                profile.LengthFeet,
                profile.WeightPounds,
                profile.Axles,
                profile.AxleWeightPounds,
                profile.Hazmat,
                Stops = load
                  .Stops.OrderBy(x => x.Sequence)
                  .Select(x => new
                  {
                    x.Id,
                    x.Sequence,
                    x.TruckId,
                    x.Latitude,
                    x.Longitude,
                    x.Address,
                    x.City,
                    x.Province,
                    x.Country,
                    x.ZipCode,
                  }),
              },
              RoutingJson.Options
            )
          )
        )
      ),
      load.Stops.Select(s => (s.Id, s.ManualCompletionRevision))
    );

  public static bool Matches(
    DispatchRoutePlan saved,
    DispatchEntity load,
    TruckRouteProfile profile
  ) => Matches(saved, RouteWorkProjection.Capture(load), profile);

  public static bool Matches(
    DispatchRoutePlan saved,
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  ) => Matches(saved.InputHash, saved.ExecutionLegId, load, profile);

  public static bool Matches(
    SavedRoutePlanMetadata saved,
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  ) => Matches(saved.InputHash, saved.ExecutionLegId, load, profile);

  private static bool Matches(
    string inputHash,
    Guid? executionLegId,
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  )
  {
    if (executionLegId != load.ExecutionLegId)
      return false;
    if (inputHash == Hash(load, profile))
      return true;
    if (load.ExecutionLegId.HasValue)
      return false;
    if (load.RouteChoiceRevision != 0)
      return false;
    if (load.Stops.Any(s => s.ManualCompletionRevision != 0))
      return false;
    if (load.Stops.Any(s => !string.IsNullOrWhiteSpace(s.Address)))
      return false;
    var legacy = Convert.ToHexString(
      SHA256.HashData(
        Encoding.UTF8.GetBytes(
          JsonSerializer.Serialize(
            new
            {
              load.TruckId,
              load.TrailerId,
              profile.HeightFeet,
              profile.WidthFeet,
              profile.LengthFeet,
              profile.WeightPounds,
              profile.Axles,
              profile.AxleWeightPounds,
              profile.Hazmat,
              Stops = load
                .Stops.OrderBy(x => x.Sequence)
                .Select(x => new
                {
                  x.Id,
                  x.Sequence,
                  x.Latitude,
                  x.Longitude,
                  x.Address,
                  x.City,
                  x.Province,
                  x.Country,
                  x.ZipCode,
                }),
            },
            RoutingJson.Options
          )
        )
      )
    );
    return inputHash == legacy;
  }
}
