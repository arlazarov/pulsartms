using Domain.Entities.Dispatch;

namespace Application.Features.Execution.Models;

public sealed record ExecutionSourceChanges(
  IReadOnlyList<DispatchStop> Stops,
  IReadOnlyList<Guid> ChangedVisitIds,
  IReadOnlyList<string> Problems
)
{
  public static ExecutionSourceChanges Preview(
    IReadOnlyList<DispatchStop> snapshot,
    IReadOnlyCollection<DispatchStop> source,
    IReadOnlySet<Guid> nativeVisitIds
  )
  {
    var stops = snapshot.Select(ExecutionSnapshots.Copy).ToArray();
    var supplied = source.ToDictionary(x => x.Id);
    var changed = new List<Guid>();
    var problems = new List<string>();
    foreach (var stop in stops.Where(x => !nativeVisitIds.Contains(x.Id)))
    {
      if (!supplied.TryGetValue(stop.Id, out var current))
      {
        problems.Add("A source visit was removed or replaced.");
        continue;
      }
      if (
        stop.Job != (current.ManualAction ?? current.Job)
        || stop.OperationRevision != current.OperationRevision
        || stop.OperationRecordedAt != current.OperationRecordedAt
        || current.ManualStateAfter is { } state && state != stop.StateAfter
      )
      {
        problems.Add(
          "Operation or cargo changes need native itinerary editing."
        );
        continue;
      }
      if (SameLocationAndSchedule(stop, current))
        continue;
      if (HasActual(stop) || HasActual(current))
      {
        problems.Add("Recorded visits cannot be moved or rescheduled.");
        continue;
      }
      if (
        string.IsNullOrWhiteSpace(current.Address)
        || current.Latitude is not (>= -90 and <= 90)
        || current.Longitude is not (>= -180 and <= 180)
      )
      {
        problems.Add("A changed visit needs a confirmed address and location.");
        continue;
      }
      stop.Name = current.Name;
      stop.Address = current.Address;
      stop.City = current.City;
      stop.Province = current.Province;
      stop.Country = current.Country;
      stop.ZipCode = current.ZipCode;
      stop.Latitude = current.Latitude;
      stop.Longitude = current.Longitude;
      stop.SourceAddressJson = current.SourceAddressJson;
      stop.AddressVerifiedAt = current.AddressVerifiedAt;
      stop.ScheduledDate = current.ScheduledDate;
      stop.ScheduledTime = current.ScheduledTime;
      stop.ScheduledDate2 = current.ScheduledDate2;
      stop.ScheduledTime2 = current.ScheduledTime2;
      stop.IsWindow = current.IsWindow;
      changed.Add(stop.Id);
    }
    return new(stops, changed, problems.Distinct().ToArray());
  }

  private static bool HasActual(DispatchStop stop) =>
    stop.ArrivedAt.HasValue
    || stop.PickedUpAt.HasValue
    || stop.DeliveredAt.HasValue
    || stop.DepartedAt.HasValue
    || stop.ManualCompletedAt.HasValue;

  private static bool SameLocationAndSchedule(
    DispatchStop retained,
    DispatchStop supplied
  ) =>
    retained.Name == supplied.Name
    && retained.Address == supplied.Address
    && retained.City == supplied.City
    && retained.Province == supplied.Province
    && retained.Country == supplied.Country
    && retained.ZipCode == supplied.ZipCode
    && retained.Latitude == supplied.Latitude
    && retained.Longitude == supplied.Longitude
    && retained.ScheduledDate == supplied.ScheduledDate
    && retained.ScheduledTime == supplied.ScheduledTime
    && retained.ScheduledDate2 == supplied.ScheduledDate2
    && retained.ScheduledTime2 == supplied.ScheduledTime2
    && retained.IsWindow == supplied.IsWindow;
}
