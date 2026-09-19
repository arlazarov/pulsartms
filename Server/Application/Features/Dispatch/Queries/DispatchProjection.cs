using System.Linq.Expressions;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Queries;

public static class DispatchProjection
{
  public static DispatchResponse Complete(
    DispatchResponse load,
    bool resolveHeader = true
  )
  {
    var start = load.PlanningFromStopId.HasValue
      ? load.Stops.SingleOrDefault(s => s.Id == load.PlanningFromStopId)
      : load
        .Stops.OrderBy(s => s.Sequence)
        .FirstOrDefault(s =>
          s.TruckId.HasValue
          || !string.IsNullOrWhiteSpace(s.TruckNumber)
          || s.ManualStateAfter is not null and not "No truck"
        );
    var operations = StopOperation
      .Resolve(
        load.Stops.Select(s => new DispatchStop
        {
          Id = s.Id,
          Sequence = s.Sequence,
          TruckId = s.TruckId,
          TruckNumber = s.TruckNumber,
          Job = s.ImportedJob ?? s.Job,
          ManualAction = s.ManualAction,
          ManualStateAfter = s.ManualStateAfter,
        }),
        load.PlanningFromStopId
      )
      .ToDictionary(s => s.Id);
    foreach (var stop in load.Stops)
    {
      stop.ImportedJob ??= stop.Job;
      var operation = operations[stop.Id];
      stop.DriverOnly =
        start is not null && stop.Sequence < start.Sequence
        || operation.StateAfter == "No truck";
      stop.Job =
        stop.DriverOnly && stop.ManualAction is null
          ? "Driver start"
          : operation.Job;
      stop.StateAfter = stop.DriverOnly ? "No truck" : operation.StateAfter;
    }
    if (resolveHeader)
      load.TruckId ??= load
        .Stops.FirstOrDefault(s => s.TruckId.HasValue)
        ?.TruckId;
    if (string.IsNullOrWhiteSpace(load.TruckNumber))
      load.TruckNumber = start?.TruckNumber ?? "";
    return load;
  }

  public static readonly Expression<
    Func<DispatchEntity, DispatchResponse>
  > Details = x => new DispatchResponse
  {
    Id = x.Id,
    DriverId = x.DriverId,
    TruckId = x.PlanningTruckId ?? x.TruckId,
    PlanningTruckId = x.PlanningTruckId,
    PlanningFromStopId = x.PlanningFromStopId,
    PlanningAssignmentRevision = x.PlanningAssignmentRevision,
    RouteChoiceRevision = x.RouteChoiceRevision,
    PlanningAssignmentRecordedAt = x.PlanningAssignmentRecordedAt,
    LoadNumber = x.LoadNumber,
    OrderNumber = x.OrderNumber,
    Status = x.Status,
    OrderDate = x.OrderDate,
    InvoiceDate = x.InvoiceDate,
    ShipDate = x.ShipDate,
    DeliveryDate = x.DeliveryDate,
    CustomerName = x.CustomerName,
    DriverName = x.DriverName,
    TruckNumber =
      x.PlanningTruck != null ? x.PlanningTruck.UnitNumber : x.TruckNumber,
    TrailerNumber = x.TrailerNumber,
    LoadedMiles = x.LoadedMiles,
    Price = x.Price,
    Currency = x.Currency,
    LastSyncedAt = x.LastSyncedAt,
    Stops = x
      .Stops.OrderBy(s => s.Sequence)
      .Select(s => new DispatchStopResponse
      {
        Id = s.Id,
        TruckId = s.TruckId,
        Sequence = s.Sequence,
        Job = s.Job,
        ImportedJob = s.Job,
        ManualAction = s.ManualAction,
        ManualStateAfter = s.ManualStateAfter,
        OperationRevision = s.OperationRevision,
        OperationRecordedAt = s.OperationRecordedAt,
        Name = s.Name,
        Address = s.Address,
        City = s.City,
        Province = s.Province,
        Country = s.Country,
        ZipCode = s.ZipCode,
        Latitude = s.Latitude,
        Longitude = s.Longitude,
        DriverName = s.DriverName,
        CoDriverName = s.CoDriverName,
        TruckNumber = s.TruckNumber,
        TrailerNumber = s.TrailerNumber,
        Commodity = s.Commodity,
        Notes = s.Notes,
        StopNo = s.StopNo,
        Weight = s.Weight,
        WeightUnit = s.WeightUnit,
        Pieces = s.Pieces,
        Pallets = s.Pallets,
        Temperature = s.Temperature,
        TemperatureUnit = s.TemperatureUnit,
        ScheduledDate = s.ScheduledDate,
        ScheduledTime = s.ScheduledTime,
        ScheduledDate2 = s.ScheduledDate2,
        ScheduledTime2 = s.ScheduledTime2,
        IsWindow = s.IsWindow,
        AppointmentTimeZoneId = s.AppointmentTimeZoneId,
        ArrivedAt = s.ArrivedAt,
        PickedUpAt = s.PickedUpAt,
        DeliveredAt = s.DeliveredAt,
        DepartedAt = s.DepartedAt,
        ManualCompletedAt = s.ManualCompletedAt,
        CompletionOverride = s.CompletionOverride,
        ManualCompletedBy = s.ManualCompletedBy,
        ManualCompletedByName = s.ManualCompletedByName,
        ManualCompletionRecordedAt = s.ManualCompletionRecordedAt,
        ManualCompletionRevision = s.ManualCompletionRevision,
      })
      .ToList(),
  };

  private static readonly Func<DispatchEntity, DispatchResponse> Project =
    Details.Compile();

  public static DispatchResponse FromSource(DispatchEntity source) =>
    Complete(Project(source));

  public static DispatchResponse FromExecution(ExecutionLoadSnapshot load)
  {
    var work = load.Work;
    var details = load.Details;
    return new()
    {
      Id = work.Id,
      ExecutionLegId = work.ExecutionLegId,
      AssignmentRevision = work.AssignmentRevision,
      ExecutionStatus = work.ExecutionStatus,
      AwaitingReceipt = work.Stops.FirstOrDefault()?.AwaitingHandoff == true,
      DriverId = work.DriverId,
      TruckId = work.TruckId,
      TruckNumber = work.TruckNumber,
      RouteChoiceRevision = work.RouteChoiceRevision,
      LoadNumber = work.LoadNumber,
      OrderNumber = details.OrderNumber,
      Status = work.Status,
      OrderDate = details.OrderDate,
      InvoiceDate = details.InvoiceDate,
      ShipDate = work.ShipDate,
      DeliveryDate = work.DeliveryDate,
      CustomerName = details.CustomerName,
      DriverName = details.DriverName,
      TrailerNumber = details.TrailerNumber,
      LoadedMiles = work.LoadedMiles,
      Price = work.Price,
      Currency = work.Currency,
      LastSyncedAt = details.LastSyncedAt,
      Stops = work
        .Stops.Select(s =>
        {
          var extra = details.Stops[s.Id];
          return new DispatchStopResponse
          {
            Id = s.Id,
            TruckId = s.TruckId,
            TruckNumber = s.TruckNumber,
            Sequence = s.Sequence,
            Job = s.Job,
            ImportedJob = s.Job,
            ManualAction = s.ManualAction,
            ManualStateAfter = s.ManualStateAfter,
            StateAfter = s.StateAfter,
            DriverOnly = s.StateAfter == "No truck",
            OperationRevision = s.OperationRevision,
            OperationRecordedAt = extra.OperationRecordedAt,
            Name = s.Name,
            Address = s.Address,
            City = s.City,
            Province = s.Province,
            Country = s.Country,
            ZipCode = s.ZipCode,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            DriverName = extra.DriverName,
            CoDriverName = extra.CoDriverName,
            TrailerNumber = extra.TrailerNumber,
            Commodity = s.Commodity,
            Notes = s.Notes,
            StopNo = extra.StopNo,
            Weight = extra.Weight,
            WeightUnit = extra.WeightUnit,
            Pieces = extra.Pieces,
            Pallets = extra.Pallets,
            Temperature = extra.Temperature,
            TemperatureUnit = extra.TemperatureUnit,
            ScheduledDate = s.ScheduledDate,
            ScheduledTime = s.ScheduledTime,
            ScheduledDate2 = s.ScheduledDate2,
            ScheduledTime2 = s.ScheduledTime2,
            IsWindow = s.IsWindow,
            AppointmentTimeZoneId = s.AppointmentTimeZoneId,
            ArrivedAt = s.ArrivedAt,
            PickedUpAt = s.PickedUpAt,
            DeliveredAt = s.DeliveredAt,
            DepartedAt = s.DepartedAt,
            ManualCompletedAt = s.ManualCompletedAt,
            CompletionOverride = s.CompletionOverride,
            ManualCompletedBy = s.ManualCompletedBy,
            ManualCompletedByName = extra.ManualCompletedByName,
            ManualCompletionRecordedAt = extra.ManualCompletionRecordedAt,
            ManualCompletionRevision = s.ManualCompletionRevision,
            ExecutionCompleted = s.ExecutionCompleted,
            AwaitingHandoff = s.AwaitingHandoff,
          };
        })
        .ToList(),
    };
  }
}
