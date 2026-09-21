using System.Text.Json;
using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Domain.Models.Execution;

public sealed record ExecutionSourceUpdate(
  IReadOnlyList<DispatchStop> Stops,
  string? ReviewReason,
  DateTime? CompletedAt,
  bool Changed,
  string Status
);

public static class ExecutionSourceFacts
{
  public static ExecutionSourceUpdate Reconcile(
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> snapshot,
    DispatchEntity source,
    IReadOnlySet<Guid> nativeVisitIds,
    bool topologyChanged,
    DateTime now,
    IReadOnlySet<Guid>? confirmedTransfers = null
  )
  {
    var stops = snapshot.Select(ExecutionSnapshots.Copy).ToList();
    var current = source.Stops.ToDictionary(x => x.Id);
    string? review = topologyChanged
      ? "Source visits were added, removed or reordered. Review the itinerary."
      : null;
    var changed = false;
    foreach (var stop in stops)
    {
      if (nativeVisitIds.Contains(stop.Id))
        continue;
      if (!current.TryGetValue(stop.Id, out var supplied))
      {
        review ??=
          "A source visit was removed or replaced. "
          + "Review the native itinerary.";
        continue;
      }
      if (!SameVisit(stop, supplied))
      {
        review ??=
          "A source address or operation changed. "
          + "Review the native itinerary.";
        continue;
      }
      // The effective source already preserves workspace appointment overrides.
      var appointment = StopAppointment.From(supplied);
      if (StopAppointment.From(stop) != appointment && !topologyChanged)
      {
        appointment.ApplyTo(stop);
        changed = true;
      }
      if (
        InvalidTime(supplied.ArrivedAt)
        || InvalidTime(supplied.PickedUpAt)
        || InvalidTime(supplied.DeliveredAt)
        || InvalidTime(supplied.DepartedAt)
        || InvalidTime(supplied.ManualCompletedAt)
      )
      {
        review ??=
          "Source actual times fall outside this active assignment. "
          + "Review the recorded events.";
        continue;
      }
      if (
        !Ordered(
          supplied.ArrivedAt,
          supplied.PickedUpAt,
          supplied.DeliveredAt,
          supplied.DepartedAt
        )
      )
      {
        review ??=
          "Source actual times are out of order. "
          + "Native history was retained for review.";
        continue;
      }
      var arrived = Merge(stop.ArrivedAt, supplied.ArrivedAt);
      var pickedUp = Merge(stop.PickedUpAt, supplied.PickedUpAt);
      var delivered = Merge(stop.DeliveredAt, supplied.DeliveredAt);
      var departed = Merge(stop.DepartedAt, supplied.DepartedAt);
      if (!Ordered(arrived, pickedUp, delivered, departed))
      {
        review ??= "Source actual times conflict with recorded native events.";
        continue;
      }
      changed |=
        stop.ArrivedAt != arrived
        || stop.PickedUpAt != pickedUp
        || stop.DeliveredAt != delivered
        || stop.DepartedAt != departed;
      stop.ArrivedAt = arrived;
      stop.PickedUpAt = pickedUp;
      stop.DeliveredAt = delivered;
      stop.DepartedAt = departed;
      var manual = Merge(stop.ManualCompletedAt, supplied.ManualCompletedAt);
      if (manual != stop.ManualCompletedAt)
      {
        changed = true;
        stop.ManualCompletedAt = manual;
        stop.ManualCompletedBy = supplied.ManualCompletedBy;
        stop.ManualCompletionRecordedAt = supplied.ManualCompletionRecordedAt;
        stop.ManualCompletionRevision = supplied.ManualCompletionRevision;
      }
    }
    if (changed && !ExecutionActualChronology.Ordered(stops))
      return new(
        snapshot.Select(ExecutionSnapshots.Copy).ToArray(),
        "Source actuals conflict with accepted visit order. Review actual execution.",
        null,
        false,
        leg.Status
      );
    var progress = new ExecutionLeg
    {
      Status = leg.Status,
      StartSwitchId = leg.StartSwitchId,
      EndSwitchId = leg.EndSwitchId,
    };
    if (review is null)
      ExecutionLegProgress.Apply(progress, stops, confirmedTransfers);
    var completed = progress.CompletedAt;
    if (completed < leg.StartedAt)
    {
      review =
        "Source delivery predates this assignment. "
        + "Review actual execution before closing it.";
      completed = null;
      progress.Status = leg.Status;
    }
    return new(stops, review, completed, changed, progress.Status);

    bool InvalidTime(DateTime? value) =>
      value.HasValue
      && (
        value > now
        || value.Value.Year < 2000
        || value < leg.StartedAt
        || leg.Status == "planned" && leg.StartSwitchId.HasValue
      );

    DateTime? Merge(DateTime? retained, DateTime? supplied)
    {
      if (!supplied.HasValue)
        return retained;
      if (retained.HasValue && supplied != retained)
      {
        review ??= "A source actual time changed. Native history needs review.";
        return retained;
      }
      return supplied;
    }
  }

  private static bool Ordered(
    DateTime? arrived,
    DateTime? pickedUp,
    DateTime? delivered,
    DateTime? departed
  ) =>
    !(arrived > pickedUp)
    && !((pickedUp ?? arrived) > delivered)
    && !((delivered ?? pickedUp ?? arrived) > departed);

  private static bool SameVisit(DispatchStop retained, DispatchStop supplied) =>
    retained.Job == (supplied.ManualAction ?? supplied.Job)
    && retained.OperationRevision == supplied.OperationRevision
    && retained.OperationRecordedAt == supplied.OperationRecordedAt
    && (
      supplied.ManualStateAfter is null
      || retained.StateAfter == supplied.ManualStateAfter
    )
    && retained.Name == supplied.Name
    && SameLocation(retained, supplied);

  private static bool SameLocation(DispatchStop retained, DispatchStop supplied)
  {
    if (
      StopAddress.From(retained) == StopAddress.From(supplied)
      && retained.Latitude == supplied.Latitude
      && retained.Longitude == supplied.Longitude
    )
      return true;
    if (retained.AddressVerifiedAt is null)
      return false;
    try
    {
      var original = JsonSerializer.Deserialize<StopAddress>(
        retained.SourceAddressJson
      );
      var observed = JsonSerializer.Deserialize<StopAddress>(
        supplied.SourceAddressJson
      );
      return original is not null
        && !string.IsNullOrWhiteSpace(original.Address)
        && original == observed
        && (
          supplied.AddressVerifiedAt is not null
          || original == StopAddress.From(supplied)
        );
    }
    catch (JsonException)
    {
      return false;
    }
  }
}
