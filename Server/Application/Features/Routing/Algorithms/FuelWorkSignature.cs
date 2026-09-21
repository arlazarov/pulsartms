using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Routing.Algorithms;

// The fingerprint of the work a fuel plan covers: the loads, their stops in
// order and where they are. A saved plan is kept only while the work it was
// calculated for is still the work - so this has to say the same thing for
// the same work, whoever asks, which is why it is not a part of the horizon
// that first needed it.
public static class FuelWorkSignature
{
  public static string LoadSignature(IWorkFacts load) =>
    load.RouteChoiceRevision == 0
      ? StopSignature(load)
      : Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{StopSignature(load)}:{load.RouteChoiceRevision}"
          )
        )
      );

  internal static string StopSignature(IWorkFacts load)
  {
    var original = LegacyStopSignature(load);
    return load.ExecutionLegId is { } legId
      ? Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{original}:{legId}:{load.AssignmentRevision}"
          )
        )
      )
      : original;
  }

  internal static string LegacyStopSignature(IWorkFacts load) =>
    StopCompletionIdentity.Revise(
      Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            new
            {
              load.Id,
              load.TruckId,
              Stops = load
                .Stops.Where(s => !s.DriverOnly)
                .OrderBy(s => s.Sequence)
                .Select(s => new
                {
                  s.Id,
                  s.Sequence,
                  s.TruckId,
                  s.Job,
                  s.StateAfter,
                  s.OperationRevision,
                  s.Address,
                  s.City,
                  s.Province,
                  s.ZipCode,
                  s.Country,
                  s.Latitude,
                  s.Longitude,
                  s.ScheduledDate,
                  s.ScheduledTime,
                  s.ScheduledDate2,
                  s.ScheduledTime2,
                  s.AppointmentTimeZoneId,
                }),
            },
            RoutingJson.Options
          )
        )
      ),
      load.Stops.Where(s => !s.DriverOnly)
        .Select(s => (s.Id, s.ManualCompletionRevision))
    );

  public static string Signature(IEnumerable<IWorkFacts> loads)
  {
    var ordered = loads.ToList();
    if (ordered.Any(x => x.ExecutionLegId.HasValue))
      return Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            ordered.Select(x => new
            {
              x.Id,
              x.ExecutionLegId,
              x.AssignmentRevision,
              x.Status,
              Signature = LoadSignature(x),
            }),
            RoutingJson.Options
          )
        )
      );
    var original = ItinerarySignature(ordered);
    return ordered.All(load => load.RouteChoiceRevision == 0)
      ? original
      : Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            new
            {
              original,
              Choices = ordered.Select(load => new
              {
                load.Id,
                load.RouteChoiceRevision,
              }),
            },
            RoutingJson.Options
          )
        )
      );
  }

  internal static string ItinerarySignature(IEnumerable<IWorkFacts> loads) =>
    StopCompletionIdentity.Revise(
      Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
              loads.Select(x => new
              {
                x.Id,
                x.TruckId,
                x.Status,
                Stops = x
                  .Stops.Where(s => !s.DriverOnly)
                  .OrderBy(s => s.Sequence)
                  .Select(s => new
                  {
                    s.Sequence,
                    s.TruckId,
                    s.Job,
                    s.StateAfter,
                    s.OperationRevision,
                    s.Address,
                    s.City,
                    s.Province,
                    s.ZipCode,
                    s.Country,
                    s.Latitude,
                    s.Longitude,
                    s.ScheduledDate,
                    s.ScheduledTime,
                    s.ScheduledDate2,
                    s.ScheduledTime2,
                    s.AppointmentTimeZoneId,
                  }),
              }),
              RoutingJson.Options
            )
          )
        )
      ),
      loads
        .SelectMany(l => l.Stops)
        .Where(s => !s.DriverOnly)
        .Select(s => (s.Id, s.ManualCompletionRevision))
    );
}
