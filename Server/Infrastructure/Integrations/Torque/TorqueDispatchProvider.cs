using System.Globalization;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Infrastructure.Integrations.Torque.Models;

namespace Infrastructure.Integrations.Torque;

public class TorqueDispatchProvider(TorqueApiService apiService)
  : IDispatchProvider
{
  public string Key => "torqueai";
  public string DisplayName => "TorqueAI";

  public async Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
    CancellationToken cancellationToken = default
  )
  {
    var dispatches = await apiService.GetDispatchesAsync(cancellationToken);
    return [.. dispatches.Select(MapDispatch)];
  }

  public async Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
    DateOnly from,
    DateOnly to,
    CancellationToken cancellationToken = default
  )
  {
    var dispatches = await apiService.GetDispatchesAsync(
      from,
      to,
      cancellationToken
    );
    return [.. dispatches.Select(MapDispatch)];
  }

  private static ExternalDispatch MapDispatch(TorqueDispatchDto source)
  {
    return new ExternalDispatch
    {
      ExternalId = source.LoadNumber.ToString(CultureInfo.InvariantCulture),
      LoadNumber = source.LoadNumber,
      OrderNumber = source.OrderNumber,
      Status = source.Status,
      OrderDate = ParseDate(source.OrderDate),
      InvoiceDate = ParseDate(source.InvoiceDate),
      ShipDate = ParseDate(source.ShipDate),
      DeliveryDate = ParseDate(source.DeliveryDate),
      CustomerName = source.CustomerName,
      DriverName = source.DriverName,
      CarrierName = source.CarrierName,
      TruckNumber = source.TruckNumber,
      TrailerNumber = source.TrailerNumber,
      LoadedMiles = source.LoadedMiles,
      Price = source.TotalCharge,
      Currency = source.Currency,
      Stops = [.. source.Stops.Select(MapStop)],
    };
  }

  private static ExternalDispatchStop MapStop(TorqueDispatchStopDto source)
  {
    return new ExternalDispatchStop
    {
      Sequence = source.Sequence,
      Job = source.Job,
      Name = source.Name,
      Address = source.Address,
      City = source.City,
      Province = source.Province,
      Country = source.Country,
      ZipCode = source.ZipCode,
      Latitude = source.Latitude,
      Longitude = source.Longitude,
      DriverName = source.DriverName,
      CoDriverName = source.CoDriverName,
      CarrierName = source.CarrierName,
      TruckNumber = source.TruckNumber,
      TrailerNumber = source.TrailerNumber,
      Commodity = source.Commodity,
      Notes = source.Notes,
      StopNo = source.StopNo,
      Weight = source.Weight,
      WeightUnit = source.WeightUnit,
      Pieces = source.Pieces,
      Pallets = source.Pallets,
      Temperature = source.Temperature,
      TemperatureUnit = source.TemperatureUnit,
      ScheduledDate = ParseDate(source.Scheduled?.PickupDate),
      ScheduledTime = ParseTime(source.Scheduled?.PickupTime),
      ScheduledDate2 = ParseDate(source.Scheduled?.PickupDate2),
      ScheduledTime2 = ParseTime(source.Scheduled?.PickupTime2),
      IsWindow = source.Scheduled?.IsWindow ?? false,
      ArrivedAt = source.Actual?.Arrived,
      PickedUpAt = source.Actual?.PickedUp,
      DeliveredAt = source.Actual?.Delivered,
      DepartedAt = source.Actual?.Departed,
    };
  }

  private static DateOnly? ParseDate(string? value)
  {
    return DateOnly.TryParse(value, out var result) ? result : null;
  }

  private static TimeOnly? ParseTime(string? value)
  {
    return TimeOnly.TryParse(value, out var result) ? result : null;
  }
}
