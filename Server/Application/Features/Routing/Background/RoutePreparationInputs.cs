using System.Security.Cryptography;
using System.Text.Json;
using Application.Caching;
using Application.Features.Routing.Services.Addresses;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Background;

public static class RoutePreparationInputs
{
  public static Guid? Truck(Load load, Guid? known = null) =>
    load.PlanningTruckId
    ?? load.TruckId
    ?? (
      load
        .Stops.Where(x => x.TruckId.HasValue)
        .Select(x => x.TruckId)
        .Distinct()
        .Take(2)
        .ToArray()
        is [var truck]
        ? truck
        : known
    );

  public static string Signature(
    Load load,
    Guid? truckId,
    long connectionVersion,
    ReadCache reads,
    DateTime now,
    long? profileGeneration = null
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            load.Id,
            load.ExecutionLegId,
            load.AssignmentRevision,
            load.ExecutionStatus,
            load.Status,
            load.TruckId,
            load.TruckNumber,
            load.ShipDate,
            load.DeliveryDate,
            load.PlanningTruckId,
            load.PlanningFromStopId,
            load.PlanningAssignmentRevision,
            load.RouteChoiceRevision,
            load.Price,
            load.Currency,
            load.LoadedMiles,
            connectionVersion,
            Profile = profileGeneration
              ?? reads.Generation($"profile:{truckId ?? Guid.Empty}"),
            Stops = load
              .Stops.OrderBy(x => x.Sequence)
              .Select(x => new
              {
                x.Id,
                x.Sequence,
                x.TruckId,
                x.Job,
                x.ManualAction,
                x.ManualStateAfter,
                x.OperationRevision,
                x.Address,
                x.City,
                x.Province,
                x.Country,
                x.ZipCode,
                x.Latitude,
                x.Longitude,
                x.SourceAddressJson,
                x.AddressVerifiedAt,
                x.AddressRetryAfter,
                Verified = StopLocation.VerifiedPoint(x, now) is not null,
                x.ScheduledDate,
                x.ScheduledTime,
                x.ScheduledDate2,
                x.ScheduledTime2,
                x.IsWindow,
                x.AppointmentTimeZoneId,
                x.DeliveredAt,
                x.DepartedAt,
                x.PickedUpAt,
                x.ManualCompletedAt,
                x.CompletionOverride,
                x.ManualCompletionRevision,
                x.AwaitingHandoff,
              }),
          }
        )
      )
    );
}
