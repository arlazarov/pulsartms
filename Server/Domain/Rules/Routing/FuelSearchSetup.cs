using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

// The numbers a fuel search runs on, worked out once from the truck, the
// road ahead and what the driver must arrive with. Everything that can be
// wrong about those inputs is refused here, so the search itself never
// has to ask whether a tank size or an arrival requirement makes sense.
public sealed record FuelSearchSetup(
  double Mpg,
  double Cap,
  int Reserve,
  double FirstMinimum,
  int ArrivalMinimum,
  bool FillBeforeUnknownArea,
  bool HasUsableExit,
  List<FuelCandidate> Stations,
  bool CanFillBeforeDelivery,
  double TopUpThreshold,
  FuelCandidate? TopUpStation
)
{
  public static FuelSearchSetup From(
    double miles,
    double currentGallons,
    TruckRouteProfile profile,
    IReadOnlyList<FuelCandidate> candidates,
    FuelArrivalPolicy? arrivalPolicy,
    double initialAccessMiles
  )
  {
    if (profile.Validate(true) is { } error)
      throw new RoutePlanningException(error);
    if (!double.IsFinite(initialAccessMiles) || initialAccessMiles < 0)
      throw new RoutePlanningException(
        "The estimated initial access distance is invalid."
      );
    var mpg = profile.Mpg!.Value;
    var cap = profile.TankGallons!.Value * (profile.FillPercent / 100);
    var reserve = (int)Math.Ceiling(profile.ReserveGallons);
    if (
      FuelReservePolicy.StartingLevelError(currentGallons, profile) is
      { } levelError
    )
      throw new RoutePlanningException(levelError);
    var firstMinimum = FuelReservePolicy.PhysicalArrivalMinimumGallons;
    var arrivalMinimum = arrivalPolicy is null
      ? reserve
      : Math.Max(reserve, (int)Math.Ceiling(arrivalPolicy.MinimumGallons));
    if (
      arrivalPolicy is not null
      && (
        !double.IsFinite(arrivalPolicy.MinimumGallons)
        || !double.IsFinite(arrivalPolicy.TargetGallons)
        || !arrivalPolicy.HasValidReplacementValue()
        || arrivalPolicy.TargetGallons < arrivalPolicy.MinimumGallons
      )
    )
      throw new RoutePlanningException(
        "Post-delivery fuel requirements are invalid."
      );
    var fillBeforeUnknownArea =
      arrivalPolicy
        is {
          PoorArea: true,
          NextDispatchId: null,
          EconomicPurchasesOnly: false
        };
    var hasUsableExit =
      arrivalPolicy is { PoorArea: false }
      && arrivalPolicy.EscapeStationId != Guid.Empty
      && double.IsFinite(arrivalPolicy.EscapeMiles)
      && arrivalPolicy.EscapeMiles >= 0
      && arrivalMinimum >= reserve + arrivalPolicy.EscapeMiles / mpg;
    var stations = candidates
      .Where(x =>
        x.AlongMiles >= 0
        && x.AlongMiles < miles
        && x.PriceUsd > 0
        && x.EconomicPriceUsd > 0
      )
      .GroupBy(x => x.VisitKey)
      .Select(g => g.MinBy(x => x.EconomicPriceUsd)!)
      .OrderBy(x => x.AlongMiles)
      .ToList();
    var canFillBeforeDelivery = stations.Any(x =>
      currentGallons
        - (initialAccessMiles + x.AlongMiles + x.ExtraInMiles) / mpg
        >= firstMinimum
      && currentGallons
        - (initialAccessMiles + x.AlongMiles + x.ExtraInMiles) / mpg
        <= cap - FuelOptimizer.MinimumAutomaticPurchaseGallons
      && cap - (miles - x.AlongMiles + x.ExtraOutMiles) / mpg >= arrivalMinimum
    );
    var topUpThreshold = profile.TankGallons.Value * .8;
    var topUpStation = arrivalPolicy
      is {
        PoorArea: true,
        TopUpPriceCeilingUsd: > 0,
        EconomicPurchasesOnly: false
      }
      ? stations.LastOrDefault(x =>
        x.EconomicPriceUsd <= arrivalPolicy.TopUpPriceCeilingUsd
        && cap - (miles - x.AlongMiles + x.ExtraOutMiles) / mpg
          >= arrivalMinimum
      )
      : null;
    return new(
      mpg,
      cap,
      reserve,
      firstMinimum,
      arrivalMinimum,
      fillBeforeUnknownArea,
      hasUsableExit,
      stations,
      canFillBeforeDelivery,
      topUpThreshold,
      topUpStation
    );
  }
}
