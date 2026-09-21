using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Domain.Models.Execution;

public static class ExecutionStopRows
{
  public static List<ExecutionLegStop> Capture(
    IEnumerable<DispatchStop> stops
  ) =>
    stops
      .Select(
        (stop, index) =>
        {
          var row = new ExecutionLegStop();
          Apply(row, ExecutionSnapshots.Copy(stop), index);
          return row;
        }
      )
      .ToList();

  public static void Replace(ExecutionLeg leg, IEnumerable<DispatchStop> stops)
  {
    var captured = stops.Select(ExecutionSnapshots.Copy).ToArray();
    if (captured.Select(x => x.Id).Distinct().Count() != captured.Length)
      throw new InvalidOperationException("Execution visits must be unique.");
    var existing = leg.Stops.ToDictionary(x => x.Id);
    var retained = captured.Select(x => x.Id).ToHashSet();
    leg.Stops.RemoveAll(x => !retained.Contains(x.Id));
    for (var index = 0; index < captured.Length; index++)
    {
      var stop = captured[index];
      if (!existing.TryGetValue(stop.Id, out var row))
      {
        row = new() { ExecutionLegId = leg.Id };
        leg.Stops.Add(row);
      }
      Apply(row, stop, index);
    }
  }

  public static List<DispatchStop> Read(ExecutionLeg leg) =>
    leg
      .Stops.OrderBy(x => x.Position)
      .Select(row => new DispatchStop
      {
        Id = row.Id,
        DispatchId = row.DispatchId,
        Sequence = row.Position + 1,
        Job = row.Job,
        StateAfter = row.StateAfter,
        OperationRevision = row.OperationRevision,
        OperationRecordedAt = row.OperationRecordedAt,
        OperationRecordedBy = row.OperationRecordedBy,
        Name = row.Name,
        Address = row.Address,
        City = row.City,
        Province = row.Province,
        Country = row.Country,
        ZipCode = row.ZipCode,
        Latitude = DecimalValue.Normalize(row.Latitude),
        Longitude = DecimalValue.Normalize(row.Longitude),
        SourceAddressJson = row.SourceAddressJson,
        AddressVerifiedAt = row.AddressVerifiedAt,
        AddressRetryAfter = row.AddressRetryAfter,
        ScheduledDate = row.ScheduledDate,
        ScheduledTime = row.ScheduledTime,
        ScheduledDate2 = row.ScheduledDate2,
        ScheduledTime2 = row.ScheduledTime2,
        IsWindow = row.IsWindow,
        AppointmentTimeZoneId = row.AppointmentTimeZoneId,
        DriverName = row.DriverName,
        CoDriverName = row.CoDriverName,
        TrailerNumber = row.TrailerNumber,
        CarrierName = row.CarrierName,
        StopNo = row.StopNo,
        Notes = row.Notes,
        Commodity = row.Commodity,
        Weight = DecimalValue.Normalize(row.Weight),
        WeightUnit = row.WeightUnit,
        Pieces = DecimalValue.Normalize(row.Pieces),
        Pallets = DecimalValue.Normalize(row.Pallets),
        Temperature = row.Temperature,
        TemperatureUnit = row.TemperatureUnit,
        ArrivedAt = row.ArrivedAt,
        PickedUpAt = row.PickedUpAt,
        DeliveredAt = row.DeliveredAt,
        DepartedAt = row.DepartedAt,
        ManualCompletedAt = row.ManualCompletedAt,
        CompletionOverride = row.CompletionOverride,
        ExecutionCompleted = row.ExecutionCompleted,
        ManualCompletionRecordedAt = row.ManualCompletionRecordedAt,
        ManualCompletionRevision = row.ManualCompletionRevision,
        ManualCompletedBy = row.ManualCompletedBy,
        ManualCompletedByName = row.ManualCompletedByName,
        TruckId = leg.TruckId,
        HasDriverOverride = row.HasDriverOverride,
        DriverId = row.HasDriverOverride ? row.DriverId : leg.DriverId,
        CoDriverId = row.HasDriverOverride ? row.CoDriverId : leg.CoDriverId,
        TrailerId = leg.TrailerId,
        TruckNumber = "",
      })
      .ToList();

  private static void Apply(
    ExecutionLegStop row,
    DispatchStop stop,
    int position
  )
  {
    row.Position = position;
    row.Id = stop.Id;
    row.DispatchId = stop.DispatchId;
    row.Job = stop.Job;
    row.StateAfter = stop.StateAfter;
    row.OperationRevision = stop.OperationRevision;
    row.OperationRecordedAt = stop.OperationRecordedAt;
    row.OperationRecordedBy = stop.OperationRecordedBy;
    row.Name = stop.Name;
    row.Address = stop.Address;
    row.City = stop.City;
    row.Province = stop.Province;
    row.Country = stop.Country;
    row.ZipCode = stop.ZipCode;
    row.Latitude = stop.Latitude;
    row.Longitude = stop.Longitude;
    row.SourceAddressJson = stop.SourceAddressJson;
    row.AddressVerifiedAt = stop.AddressVerifiedAt;
    row.AddressRetryAfter = stop.AddressRetryAfter;
    row.ScheduledDate = stop.ScheduledDate;
    row.ScheduledTime = stop.ScheduledTime;
    row.ScheduledDate2 = stop.ScheduledDate2;
    row.ScheduledTime2 = stop.ScheduledTime2;
    row.IsWindow = stop.IsWindow;
    row.AppointmentTimeZoneId = stop.AppointmentTimeZoneId;
    row.HasDriverOverride = stop.HasDriverOverride;
    row.DriverId = stop.HasDriverOverride ? stop.DriverId : null;
    row.CoDriverId = stop.HasDriverOverride ? stop.CoDriverId : null;
    row.DriverName = stop.DriverName;
    row.CoDriverName = stop.CoDriverName;
    row.TrailerNumber = stop.TrailerNumber;
    row.CarrierName = stop.CarrierName;
    row.StopNo = stop.StopNo;
    row.Notes = stop.Notes;
    row.Commodity = stop.Commodity;
    row.Weight = stop.Weight;
    row.WeightUnit = stop.WeightUnit;
    row.Pieces = stop.Pieces;
    row.Pallets = stop.Pallets;
    row.Temperature = stop.Temperature;
    row.TemperatureUnit = stop.TemperatureUnit;
    row.ArrivedAt = stop.ArrivedAt;
    row.PickedUpAt = stop.PickedUpAt;
    row.DeliveredAt = stop.DeliveredAt;
    row.DepartedAt = stop.DepartedAt;
    row.ManualCompletedAt = stop.ManualCompletedAt;
    row.CompletionOverride = stop.CompletionOverride;
    row.ExecutionCompleted = stop.ExecutionCompleted;
    row.ManualCompletionRecordedAt = stop.ManualCompletionRecordedAt;
    row.ManualCompletionRevision = stop.ManualCompletionRevision;
    row.ManualCompletedBy = stop.ManualCompletedBy;
    row.ManualCompletedByName = stop.ManualCompletedByName;
  }
}
