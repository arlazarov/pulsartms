using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Services;

public static class DispatchWorkspaceData
{
  public static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public static string Write<T>(T value) =>
    JsonSerializer.Serialize(value, Json);

  public static T Read<T>(string json)
    where T : new() => JsonSerializer.Deserialize<T>(json, Json) ?? new();

  public static string Hash<T>(T value) =>
    Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json))
    );

  public static object Commercial(DispatchEntity load) =>
    new
    {
      load.OrderNumber,
      load.CustomerName,
      load.Price,
      load.Currency,
    };

  public static object Commercial(DispatchWorkspaceMetadata metadata) =>
    new
    {
      metadata.OrderNumber,
      metadata.CustomerName,
      metadata.Price,
      metadata.Currency,
    };

  public static void ApplyCommercial(
    DispatchEntity load,
    DispatchWorkspaceMetadata metadata
  )
  {
    load.OrderNumber = metadata.OrderNumber;
    if (load.CustomerName != metadata.CustomerName)
    {
      load.Customer = null;
      load.CustomerId = null;
    }
    load.CustomerName = metadata.CustomerName;
    load.Price = metadata.Price;
    load.Currency = metadata.Currency;
  }

  public static DispatchWorkspaceStop Stop(
    DispatchStop stop,
    DispatchWorkspaceStop? extras = null
  ) =>
    new()
    {
      Id = stop.Id,
      TruckId = stop.TruckId,
      TrailerId = stop.TrailerId,
      DriverId = stop.DriverId,
      CoDriverId = stop.CoDriverId,
      Sequence = stop.Sequence,
      Job = stop.Job,
      Name = stop.Name,
      Address = stop.Address,
      City = stop.City,
      Province = stop.Province,
      Country = stop.Country,
      ZipCode = stop.ZipCode,
      Latitude = stop.Latitude,
      Longitude = stop.Longitude,
      StopNo = stop.StopNo,
      AppointmentReference = extras?.AppointmentReference ?? "",
      ContactName = extras?.ContactName ?? "",
      ContactPhone = extras?.ContactPhone ?? "",
      ContactEmail = extras?.ContactEmail ?? "",
      Notes = stop.Notes,
      Commodity = stop.Commodity,
      Weight = stop.Weight,
      WeightUnit = stop.WeightUnit,
      Pieces = stop.Pieces,
      Pallets = stop.Pallets,
      Temperature = stop.Temperature,
      TemperatureUnit = stop.TemperatureUnit,
      AppointmentMode =
        stop.IsWindow ? "window"
        : stop.ScheduledDate.HasValue ? "at"
        : "unscheduled",
      TimeZoneId = stop.AppointmentTimeZoneId,
      ScheduledDate = stop.ScheduledDate,
      ScheduledTime = stop.ScheduledTime,
      ScheduledDate2 = stop.ScheduledDate2,
      ScheduledTime2 = stop.ScheduledTime2,
      TruckNumber = stop.TruckNumber,
      TrailerNumber = stop.TrailerNumber,
      DriverName = stop.DriverName,
      CoDriverName = stop.CoDriverName,
    };

  public static void Apply(DispatchStop stop, DispatchWorkspaceStop value)
  {
    stop.Job = value.Job;
    stop.Name = value.Name;
    stop.Address = value.Address;
    stop.City = value.City;
    stop.Province = value.Province;
    stop.Country = value.Country;
    stop.ZipCode = value.ZipCode;
    stop.Latitude = value.Latitude;
    stop.Longitude = value.Longitude;
    stop.StopNo = value.StopNo;
    stop.Notes = value.Notes;
    stop.Commodity = value.Commodity;
    stop.Weight = value.Weight;
    stop.WeightUnit = value.WeightUnit;
    stop.Pieces = value.Pieces;
    stop.Pallets = value.Pallets;
    stop.Temperature = value.Temperature;
    stop.TemperatureUnit = value.TemperatureUnit;
    stop.AppointmentTimeZoneId = value.TimeZoneId;
    stop.ScheduledDate = value.ScheduledDate;
    stop.ScheduledTime = value.ScheduledTime;
    stop.ScheduledDate2 = value.ScheduledDate2;
    stop.ScheduledTime2 = value.ScheduledTime2;
    stop.IsWindow = value.AppointmentMode == "window";
  }

  public static string EditableHash(DispatchWorkspaceStop value)
  {
    var canonical = new DispatchStop { Id = value.Id };
    Apply(canonical, value);
    return Hash(Stop(canonical, value));
  }

  public static string OperationalHash(DispatchWorkspaceStop value)
  {
    var canonical = new DispatchStop { Id = value.Id };
    Apply(canonical, value);
    return Hash(Stop(canonical));
  }

  public static string Fingerprint(
    DispatchEntity load,
    IEnumerable<(Guid Id, long Revision, string Stops)> legs,
    string? sourceAssignmentSignature = null
  ) =>
    Hash(
      new
      {
        Source = ExecutionSnapshots.Fingerprint(load),
        SourceAssignment = sourceAssignmentSignature,
        Commercial = Commercial(load),
        Stops = load.Stops.OrderBy(x => x.Sequence).Select(x => Stop(x)),
        Legs = legs.Select(x => new
        {
          x.Id,
          x.Revision,
          x.Stops,
        }),
      }
    );
}
