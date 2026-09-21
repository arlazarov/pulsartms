using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Models;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

// What a road is built from, written down as one string. Two roads with
// the same signature were asked for under the same conditions, so a saved
// one may be used again; anything that would change the road must appear
// here or it will be reused when it should not be.
public sealed partial class BaseRouteService
{
  public static string Signature(
    DispatchEntity load,
    TruckRouteProfile profile
  ) => Signature(RouteWorkProjection.Capture(load.TruckItinerary()), profile);

  public static string Signature(
    RouteWorkSnapshot work,
    TruckRouteProfile profile
  ) =>
    work.RouteChoiceRevision == 0
      ? ChoiceInputs(work, profile)
      : Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{ChoiceInputs(work, profile)}:{work.RouteChoiceRevision}"
          )
        )
      );

  public static string ChoiceInputs(
    DispatchEntity load,
    TruckRouteProfile profile
  ) =>
    ChoiceInputs(RouteWorkProjection.Capture(load.TruckItinerary()), profile);

  public static string ChoiceInputs(
    RouteWorkSnapshot work,
    TruckRouteProfile profile
  ) =>
    work.ExecutionLegId is { } legId
      ? Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{legId}:{work.AssignmentRevision}:"
              + ItinerarySignature(work, profile)
          )
        )
      )
      : ItinerarySignature(work, profile);

  private static string ItinerarySignature(
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        Encoding.UTF8.GetBytes(
          JsonSerializer.Serialize(
            new
            {
              LocationPolicy = "google-street-address-v1",
              profile.HeightFeet,
              profile.WidthFeet,
              profile.LengthFeet,
              profile.WeightPounds,
              profile.Axles,
              profile.AxleWeightPounds,
              profile.Hazmat,
              Stops = load
                .Stops.OrderBy(s => s.Sequence)
                .Select(s => new
                {
                  s.Sequence,
                  Latitude = DecimalValue.Normalize(s.Latitude),
                  Longitude = DecimalValue.Normalize(s.Longitude),
                  s.Address,
                  s.City,
                  s.Province,
                  s.Country,
                  s.ZipCode,
                }),
            },
            RoutingJson.Options
          )
        )
      )
    );
}
