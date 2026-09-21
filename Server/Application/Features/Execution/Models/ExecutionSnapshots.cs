using System.Security.Cryptography;
using System.Text.Json;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Models;

public sealed record ExecutionAssignment(
  Guid TruckId,
  Guid? DriverId,
  Guid? TrailerId,
  Guid? CoDriverId = null
);

public static class ExecutionSnapshots
{
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public static string Fingerprint(DispatchEntity load) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            load.Id,
            load.Status,
            load.TruckId,
            load.TruckNumber,
            load.DriverId,
            load.DriverName,
            load.TrailerId,
            load.TrailerNumber,
            load.PlanningTruckId,
            load.PlanningFromStopId,
            load.PlanningAssignmentRevision,
            Stops = load
              .Stops.OrderBy(s => s.Sequence)
              .Select(s => new
              {
                s.Id,
                s.Sequence,
                s.Job,
                s.ManualAction,
                s.ManualStateAfter,
                s.OperationRevision,
                s.TruckId,
                s.TruckNumber,
                s.DriverId,
                s.DriverName,
                s.CoDriverId,
                s.CoDriverName,
                s.TrailerId,
                s.TrailerNumber,
                s.Name,
                s.Address,
                s.City,
                s.Province,
                s.Country,
                s.ZipCode,
                s.Latitude,
                s.Longitude,
                s.ScheduledDate,
                s.ScheduledTime,
                s.ScheduledDate2,
                s.ScheduledTime2,
                s.IsWindow,
                s.AppointmentTimeZoneId,
                s.ArrivedAt,
                s.PickedUpAt,
                s.DeliveredAt,
                s.DepartedAt,
                s.ManualCompletedAt,
                s.CompletionOverride,
                s.ManualCompletionRevision,
              }),
          },
          Json
        )
      )
    );

  public static string Write(IEnumerable<DispatchStop> stops) =>
    JsonSerializer.Serialize(stops.Select(Copy), Json);

  public static List<DispatchStop> Read(string json)
  {
    var stops =
      JsonSerializer.Deserialize<List<DispatchStop>>(json, Json) ?? [];
    if (stops.Any(stop => stop is null))
      throw new JsonException("Execution snapshot contains an empty visit.");
    return stops;
  }

  public static void ApplyActual(
    DispatchStop stop,
    ExecutionTransferVisit visit
  )
  {
    if (visit.Id != stop.Id)
      return;
    stop.ArrivedAt = null;
    stop.PickedUpAt = null;
    stop.DeliveredAt = null;
    stop.DepartedAt = null;
    stop.CompletionOverride = null;
    stop.ManualCompletedAt = visit.ConfirmedBy.HasValue ? visit.ActualAt : null;
    stop.ExecutionCompleted = visit.ConfirmedBy.HasValue;
    stop.ManualCompletedBy = visit.ConfirmedBy;
    stop.ManualCompletionRevision = visit.Revision;
  }

  public static DispatchStop Boundary(
    ExecutionTransferVisit visit,
    Guid load,
    int sequence,
    string cargo
  ) =>
    new()
    {
      Id = visit.Id,
      DispatchId = load,
      Sequence = sequence,
      Job = visit.Operation,
      Name = visit.SiteName,
      Address = visit.SiteName[..Math.Min(500, visit.SiteName.Length)],
      Latitude = visit.Latitude,
      Longitude = visit.Longitude,
      ScheduledDate = visit.PlannedAt is { } time
        ? DateOnly.FromDateTime(time)
        : null,
      StateAfter = visit.Operation == "Drop" ? "Bobtail" : cargo,
    };

  public static DispatchStop Copy(DispatchStop s) =>
    new()
    {
      Id = s.Id,
      DispatchId = s.DispatchId,
      Sequence = s.Sequence,
      Job = s.ManualAction ?? s.Job,
      StateAfter = s.StateAfter,
      OperationRevision = s.OperationRevision,
      OperationRecordedAt = s.OperationRecordedAt,
      OperationRecordedBy = s.OperationRecordedBy,
      Name = s.Name,
      Address = s.Address,
      City = s.City,
      Province = s.Province,
      Country = s.Country,
      ZipCode = s.ZipCode,
      Latitude = DecimalValue.Normalize(s.Latitude),
      Longitude = DecimalValue.Normalize(s.Longitude),
      SourceAddressJson = s.SourceAddressJson,
      AddressVerifiedAt = s.AddressVerifiedAt,
      AddressRetryAfter = s.AddressRetryAfter,
      ScheduledDate = s.ScheduledDate,
      ScheduledTime = s.ScheduledTime,
      ScheduledDate2 = s.ScheduledDate2,
      ScheduledTime2 = s.ScheduledTime2,
      IsWindow = s.IsWindow,
      AppointmentTimeZoneId = s.AppointmentTimeZoneId,
      TruckId = s.TruckId,
      TruckNumber = s.TruckNumber,
      HasDriverOverride = s.HasDriverOverride,
      DriverId = s.DriverId,
      DriverName = s.DriverName,
      CoDriverId = s.CoDriverId,
      CoDriverName = s.CoDriverName,
      TrailerId = s.TrailerId,
      TrailerNumber = s.TrailerNumber,
      CarrierName = s.CarrierName,
      StopNo = s.StopNo,
      Notes = s.Notes,
      Commodity = s.Commodity,
      Weight = DecimalValue.Normalize(s.Weight),
      WeightUnit = s.WeightUnit,
      Pieces = DecimalValue.Normalize(s.Pieces),
      Pallets = DecimalValue.Normalize(s.Pallets),
      Temperature = s.Temperature,
      TemperatureUnit = s.TemperatureUnit,
      ArrivedAt = s.ArrivedAt,
      PickedUpAt = s.PickedUpAt,
      DeliveredAt = s.DeliveredAt,
      DepartedAt = s.DepartedAt,
      ManualCompletedAt = s.ManualCompletedAt,
      CompletionOverride = s.CompletionOverride,
      ExecutionCompleted = s.ExecutionCompleted,
      ManualCompletionRecordedAt = s.ManualCompletionRecordedAt,
      ManualCompletionRevision = s.ManualCompletionRevision,
      ManualCompletedBy = s.ManualCompletedBy,
      ManualCompletedByName = s.ManualCompletedByName,
    };
}
