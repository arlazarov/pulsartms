using System.Linq.Expressions;
using Application.Features.Dispatch.Models;

namespace Application.Features.Dispatch.Queries;

public static class DispatchProjection
{
  public static readonly Expression<Func<Domain.Entities.Dispatch.Dispatch, DispatchResponse>> Details = x => new DispatchResponse
      {
        Id = x.Id,
        TruckId = x.TruckId,
        LoadNumber = x.LoadNumber,
        OrderNumber = x.OrderNumber,
        Status = x.Status,
        OrderDate = x.OrderDate,
        InvoiceDate = x.InvoiceDate,
        ShipDate = x.ShipDate,
        DeliveryDate = x.DeliveryDate,
        CustomerName = x.CustomerName,
        DriverName = x.DriverName,
        TruckNumber = x.TruckNumber,
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
            ArrivedAt = s.ArrivedAt,
            PickedUpAt = s.PickedUpAt,
            DeliveredAt = s.DeliveredAt,
            DepartedAt = s.DepartedAt,
          })
          .ToList(),
      };
}
