using System.Collections.Immutable;
using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Domain.Rules.Routing;

public static class RouteWorkProjection
{
  public static RouteWorkSnapshot TruckItinerary(RouteWorkSnapshot work)
  {
    if (work.ExecutionLegId.HasValue)
      return work;
    var path = TruckPath.Resolve(
      work.TruckId,
      work.TruckNumber,
      work.PlanningTruckId,
      work.PlanningFromStopId,
      work.Stops,
      (stop, job, state) => stop with { Job = job, StateAfter = state }
    );
    return work with
    {
      TruckId = path.TruckId,
      TruckNumber = path.TruckNumber,
      Stops = path.Stops.ToImmutableArray(),
    };
  }

  public static RouteWorkSnapshot Capture(
    Load source,
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops
  ) =>
    new(
      source.Id,
      leg.TruckId,
      "",
      source.Status,
      leg.Id,
      leg.Status,
      leg.Revision,
      null,
      null,
      0,
      leg.RouteChoiceRevision,
      source.ShipDate,
      source.DeliveryDate,
      source.Price,
      source.Currency,
      source.LoadedMiles,
      stops
        .OrderBy(x => x.Sequence)
        .ThenBy(x => x.Id)
        .Select(CaptureStop)
        .ToImmutableArray()
    )
    {
      LoadNumber = source.LoadNumber,
      DriverId = (
        stops.FirstOrDefault(x => !x.IsCompleted) ?? stops.LastOrDefault()
      )
        is { } current
        ? current.HasDriverOverride
          ? current.DriverId
          : leg.DriverId
        : leg.DriverId,
      CoDriverId = (
        stops.FirstOrDefault(x => !x.IsCompleted) ?? stops.LastOrDefault()
      )
        is { HasDriverOverride: true } currentCo
        ? currentCo.CoDriverId
        : leg.CoDriverId,
      TrailerId = leg.TrailerId,
    };

  public static RouteWorkSnapshot Capture(Load load) =>
    new(
      load.Id,
      load.TruckId,
      load.TruckNumber,
      load.Status,
      load.ExecutionLegId,
      load.ExecutionStatus,
      load.AssignmentRevision,
      load.PlanningTruckId,
      load.PlanningFromStopId,
      load.PlanningAssignmentRevision,
      load.RouteChoiceRevision,
      load.ShipDate,
      load.DeliveryDate,
      load.Price,
      load.Currency,
      load.LoadedMiles,
      load.Stops.OrderBy(x => x.Sequence)
        .ThenBy(x => x.Id)
        .Select(CaptureStop)
        .ToImmutableArray()
    )
    {
      LoadNumber = load.LoadNumber,
      DriverId = load.DriverId,
      TrailerId = load.TrailerId,
    };

  public static RouteWorkSnapshot Capture(
    TruckWorkSegment segment,
    string truckNumber
  )
  {
    var stops = segment
      .Visits.Where(x => x.InTruckPath)
      .Select(CaptureStop)
      .ToImmutableArray();
    return new(
      segment.Work.DispatchId,
      segment.AssignedTruckId,
      truckNumber,
      segment.Status switch
      {
        "active" => "in_transit",
        "planned" when segment.Work.ExecutionLegId.HasValue => "assigned",
        _ => segment.Status,
      },
      segment.Work.ExecutionLegId,
      segment.Work.ExecutionLegId.HasValue ? segment.Status : null,
      segment.Work.ExecutionLegId.HasValue ? segment.AssignmentRevision : 0,
      null,
      segment.Work.ExecutionLegId.HasValue ? null : stops.FirstOrDefault()?.Id,
      segment.AssignmentRevision,
      segment.RouteChoiceRevision,
      segment.ShipDate,
      segment.DeliveryDate,
      null,
      "",
      null,
      stops
    )
    {
      LoadNumber = segment.LoadNumber,
      DriverId = segment.DriverId,
      CoDriverId = segment.CoDriverId,
      TrailerId = segment.TrailerId,
    };
  }

  private static RouteWorkStop CaptureStop(WorkVisitFacts visit) =>
    new(
      visit.Id,
      visit.TruckId,
      "",
      visit.Sequence,
      visit.Operation,
      visit.ManualAction,
      visit.ManualStateAfter,
      visit.StateAfter,
      visit.OperationRevision,
      visit.Actuals.AwaitingHandoff,
      visit.Actuals.ExecutionConfirmed,
      visit.Appointment.Date,
      visit.Appointment.Time,
      visit.Actuals.PickedUpAt,
      visit.Actuals.DeliveredAt,
      visit.Actuals.DepartedAt,
      visit.Actuals.ConfirmedAt,
      visit.Actuals.CompletionOverride,
      visit.Actuals.CompletionRevision,
      visit.Location.Name,
      visit.Location.Address,
      visit.Location.City,
      visit.Location.Province,
      visit.Location.Country,
      visit.Location.ZipCode,
      visit.Location.Latitude,
      visit.Location.Longitude,
      visit.Location.VerifiedAt,
      visit.Location.RetryAfter,
      visit.Location.Source is { } source
        ? JsonSerializer.Serialize(source)
        : ""
    )
    {
      DriverId = visit.DriverId,
      CoDriverId = visit.CoDriverId,
      TrailerId = visit.TrailerId,
      ArrivedAt = visit.Actuals.ArrivedAt,
      ManualCompletedBy = visit.Actuals.ConfirmedBy,
      ScheduledDate2 = visit.Appointment.EndDate,
      ScheduledTime2 = visit.Appointment.EndTime,
      IsWindow = visit.Appointment.IsWindow,
      AppointmentTimeZoneId = visit.Appointment.TimeZoneId,
      Commodity = visit.Commodity,
      Notes = visit.Notes,
    };

  public static RouteWorkStop CaptureStop(DispatchStop stop) =>
    new(
      stop.Id,
      stop.TruckId,
      stop.TruckNumber,
      stop.Sequence,
      stop.Job,
      stop.ManualAction,
      stop.ManualStateAfter,
      stop.StateAfter,
      stop.OperationRevision,
      stop.AwaitingHandoff,
      stop.ExecutionCompleted,
      stop.ScheduledDate,
      stop.ScheduledTime,
      stop.PickedUpAt,
      stop.DeliveredAt,
      stop.DepartedAt,
      stop.ManualCompletedAt,
      stop.CompletionOverride,
      stop.ManualCompletionRevision,
      stop.Name,
      stop.Address,
      stop.City,
      stop.Province,
      stop.Country,
      stop.ZipCode,
      stop.Latitude,
      stop.Longitude,
      stop.AddressVerifiedAt,
      stop.AddressRetryAfter,
      stop.SourceAddressJson
    )
    {
      DriverId = stop.DriverId,
      CoDriverId = stop.CoDriverId,
      TrailerId = stop.TrailerId,
      ArrivedAt = stop.ArrivedAt,
      ManualCompletedBy = stop.ManualCompletedBy,
      ScheduledDate2 = stop.ScheduledDate2,
      ScheduledTime2 = stop.ScheduledTime2,
      IsWindow = stop.IsWindow,
      AppointmentTimeZoneId = stop.AppointmentTimeZoneId,
      Commodity = stop.Commodity,
      Notes = stop.Notes,
    };
}
