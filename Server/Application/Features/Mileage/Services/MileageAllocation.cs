using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

public sealed record MovementAllocation(
  Guid? DispatchId,
  string Target,
  string Reason
);

public static class MileageAllocation
{
  public static bool ValidPurpose(string? value) =>
    value
      is "pickup-approach"
        or "delivery"
        or "yard-return"
        or "home"
        or "maintenance"
        or "reposition";

  public static bool ValidCargoState(string? value) =>
    value is "loaded" or "empty" or "bobtail" or "unknown";

  public static bool ValidPolicyTarget(string? value) =>
    value is "previous" or "next" or "unallocated";

  public static MovementAllocation Resolve(
    Movement movement,
    MileageAllocationPolicy policy
  )
  {
    if (movement.CargoState == "loaded")
      return ForTarget(movement, "carried", "carried-load");
    if (movement.Purpose == "pickup-approach")
      return ForTarget(movement, "next", "pickup-approach");
    var target = movement.Purpose switch
    {
      "yard-return" => policy.YardReturn,
      "home" => policy.Home,
      "maintenance" => policy.Maintenance,
      "reposition" => policy.Reposition,
      _ => "unallocated",
    };
    return ForTarget(movement, target, $"purpose-policy:{movement.Purpose}");
  }

  public static MovementAllocation ForTarget(
    Movement movement,
    string target,
    string reason
  )
  {
    var dispatch = target switch
    {
      "previous" => movement.PreviousDispatchId,
      "next" => movement.NextDispatchId,
      "carried" => movement.CarriedDispatchId,
      _ => null,
    };
    return new(
      dispatch,
      dispatch.HasValue ? target : "unallocated",
      dispatch.HasValue || target == "unallocated"
        ? reason
        : $"missing-{target}-load"
    );
  }
}
