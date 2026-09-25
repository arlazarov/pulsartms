using Domain.Models.Eta;
using Domain.Models.Routing;

namespace Domain.Rules.Eta;

// The owner's prediction assumption (September 25): a driver moving in
// personal conveyance close to the next stop is driving to it before going
// on duty, so that one arrival is forecast without the pre-trip, which is
// planned after the stop instead. It is an ETA assumption only - not a
// legal personal conveyance allowance, not evidence that an inspection was
// done - and it changes no ELD or HOS record and grants no hours.
//
// It holds only on a fresh personal conveyance status, fresh telemetry of
// the truck moving on its road, and a known road distance to that stop
// within the limit. Parked in personal conveyance, stale or unknown
// telemetry, off the road, or farther away: the pre-trip stays first.
public static class PersonalConveyanceApproach
{
  public const string Status = "personalConveyance";
  public const double MovingMph = 3;
  public const double MilesPerKilometre = 0.621371192;
  public static readonly TimeSpan FreshStatus = TimeSpan.FromMinutes(3);

  public static bool Qualifies(
    DriverDutyStatus? duty,
    RouteProgress? progress,
    double? milesToNextStop,
    DateTime now,
    double limitKilometres
  ) =>
    duty is { Status: Status } status
    && status.ObservedAt.UtcDateTime >= now - FreshStatus
    && status.ObservedAt.UtcDateTime <= now.AddMinutes(1)
    && progress
      is { LocationStale: false, OffRoute: false, SpeedMph: >= MovingMph }
    && milesToNextStop is { } miles
    && double.IsFinite(miles)
    && miles >= 0
    && miles <= limitKilometres * MilesPerKilometre;
}
