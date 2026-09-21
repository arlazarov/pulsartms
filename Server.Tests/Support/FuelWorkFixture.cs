using System.Collections.Immutable;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Execution;

namespace Server.Tests.Support;

internal static class FuelWorkFixture
{
  public static FuelWorkInputs Capture(
    Guid truckId,
    IEnumerable<DispatchResponse> loads
  ) =>
    new(
      new(
        truckId,
        DateTimeOffset.UtcNow,
        "fixture",
        new("Fuel", true, null, null, 0),
        loads
          .Select(load => new TruckWorkSegment(
            new(load.Id, load.ExecutionLegId),
            load.ExecutionStatus
              ?? (string.IsNullOrEmpty(load.Status) ? "assigned" : load.Status),
            load.LoadNumber,
            new(WorkActivity.Upcoming, DateTime.MaxValue, load.LoadNumber),
            load.TruckId,
            null,
            null,
            null,
            load.AssignmentRevision,
            load.RouteChoiceRevision,
            load.ShipDate,
            load.DeliveryDate,
            false,
            null,
            null,
            null,
            load.Stops.Select(stop => new WorkVisitFacts(
                stop.Id,
                stop.Id,
                stop.Sequence,
                stop.Job,
                stop.StateAfter,
                stop.ManualAction,
                stop.ManualStateAfter,
                !stop.DriverOnly,
                stop.Commodity,
                stop.Notes,
                new(
                  stop.Name,
                  stop.Address,
                  stop.City,
                  stop.Province,
                  stop.Country,
                  stop.ZipCode,
                  stop.Latitude,
                  stop.Longitude,
                  null,
                  null,
                  null
                ),
                new(
                  stop.ScheduledDate,
                  stop.ScheduledTime,
                  stop.ScheduledDate2,
                  stop.ScheduledTime2,
                  stop.IsWindow,
                  stop.AppointmentTimeZoneId
                ),
                new(
                  stop.ArrivedAt,
                  stop.PickedUpAt,
                  stop.DeliveredAt,
                  stop.DepartedAt,
                  stop.ManualCompletedAt,
                  stop.ManualCompletedBy,
                  stop.ExecutionCompleted,
                  stop.CompletionOverride,
                  stop.ManualCompletionRevision,
                  load.AwaitingReceipt,
                  stop.IsCompleted
                ),
                stop.TruckId,
                null,
                null,
                null,
                stop.OperationRevision
              ))
              .ToImmutableArray(),
            []
          ))
          .ToImmutableArray(),
        WorkSequenceEvidence.Empty,
        new([], [], [])
      )
    );
}
