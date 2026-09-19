using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Mileage.Services;

public static class OdometerEvidence
{
  public const decimal MetersPerMile = 1609.344m;

  public static string? Validate(
    DateTime start,
    DateTime end,
    decimal startMeters,
    decimal endMeters
  )
  {
    if (end <= start)
      return "non-increasing-sample-time";
    if (startMeters < 0 || endMeters < startMeters)
      return "odometer-reset-or-invalid-reading";
    if (end - start > TimeSpan.FromHours(6))
      return "odometer-sample-gap";
    var hours = (decimal)(end - start).TotalHours;
    if (endMeters - startMeters > 120m * hours * MetersPerMile + 100m)
      return "implausible-odometer-increase";
    return null;
  }

  public static decimal Miles(decimal startMeters, decimal endMeters) =>
    decimal.Round((endMeters - startMeters) / MetersPerMile, 3);

  public static int Segment(
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops,
    DateTime start,
    DateTime end
  )
  {
    var lower =
      leg.StartedAt is { } began && began > leg.RecordedAt
        ? began
        : leg.RecordedAt;
    if (
      leg.Status is not ("active" or "completed")
      || leg.SourceReviewReason is not null
      || start < lower
      || end <= start
      || leg.CompletedAt is { } completed && end > completed
    )
      return -1;
    for (var index = 0; index < stops.Count - 1; index++)
    {
      var from = stops[index];
      var to = stops[index + 1];
      if (from.CompletionOverride == false || to.CompletionOverride == false)
        continue;
      var departed =
        from.DepartedAt
        ?? from.DeliveredAt
        ?? from.PickedUpAt
        ?? from.ManualCompletedAt;
      var arrived =
        to.ArrivedAt
        ?? to.DeliveredAt
        ?? to.PickedUpAt
        ?? to.ManualCompletedAt
        ?? to.DepartedAt;
      if (
        departed.HasValue
        && arrived.HasValue
        && departed.Value <= start
        && end <= arrived.Value
        && !from.AwaitingHandoff
        && !to.AwaitingHandoff
        && (
          from.OperationRecordedAt is null || from.OperationRecordedAt <= start
        )
      )
        return index;
    }
    return -1;
  }
}
