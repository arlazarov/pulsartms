using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public static class DispatchMapper
{
  public static void Update(
    DispatchEntity dispatch,
    ExternalDispatch source,
    Customer? customer,
    Driver? driver,
    Truck? truck,
    Trailer? trailer,
    DateTime syncedAt
  )
  {
    dispatch.OrderNumber = source.OrderNumber;
    dispatch.Status = source.Status;
    dispatch.OrderDate = source.OrderDate;
    dispatch.InvoiceDate = source.InvoiceDate;
    dispatch.ShipDate = source.ShipDate;
    dispatch.DeliveryDate = source.DeliveryDate;
    dispatch.Customer = customer;
    dispatch.CustomerId = customer?.Id;
    dispatch.CustomerName = source.CustomerName;
    dispatch.Driver = driver;
    dispatch.DriverId = driver?.Id;
    dispatch.DriverName = source.DriverName;
    dispatch.CarrierName = source.CarrierName;
    dispatch.Truck = truck;
    dispatch.TruckId = truck?.Id;
    dispatch.TruckNumber = source.TruckNumber;
    dispatch.Trailer = trailer;
    dispatch.TrailerId = trailer?.Id;
    dispatch.TrailerNumber = source.TrailerNumber;
    dispatch.LoadedMiles = source.LoadedMiles;
    dispatch.Price = source.Price;
    dispatch.Currency = source.Currency;
  }

  public static DispatchStop CreateStop(
    ExternalDispatchStop source,
    Driver? driver,
    Driver? coDriver,
    Truck? truck,
    Trailer? trailer
  )
  {
    var stop = new DispatchStop { Id = Guid.NewGuid() };
    UpdateStop(stop, source, driver, coDriver, truck, trailer);
    return stop;
  }

  public static void UpdateStop(
    DispatchStop stop,
    ExternalDispatchStop source,
    Driver? driver,
    Driver? coDriver,
    Truck? truck,
    Trailer? trailer
  )
  {
    stop.Sequence = source.Sequence;
    stop.Job = source.Job;
    stop.Name = source.Name;
    var address = new StopAddress(
      source.Address,
      source.City,
      source.Province,
      source.Country,
      source.ZipCode
    );
    var sourceJson = address.Serialize();
    if (stop.SourceAddressJson != sourceJson)
    {
      address.Apply(stop);
      stop.SourceAddressJson = sourceJson;
      stop.AddressVerifiedAt = null;
      stop.AddressRetryAfter = null;
    }
    if (!stop.AddressVerifiedAt.HasValue)
    {
      stop.Latitude = source.Latitude;
      stop.Longitude = source.Longitude;
    }
    stop.Driver = driver;
    stop.DriverId = driver?.Id;
    stop.DriverName = source.DriverName;
    stop.CoDriver = coDriver;
    stop.CoDriverId = coDriver?.Id;
    stop.CoDriverName = source.CoDriverName;
    stop.CarrierName = source.CarrierName;
    stop.Truck = truck;
    stop.TruckId = truck?.Id;
    stop.TruckNumber = source.TruckNumber;
    stop.Trailer = trailer;
    stop.TrailerId = trailer?.Id;
    stop.TrailerNumber = source.TrailerNumber;
    stop.Commodity = source.Commodity;
    stop.Notes = source.Notes;
    stop.StopNo = source.StopNo;
    stop.Weight = source.Weight;
    stop.WeightUnit = source.WeightUnit;
    stop.Pieces = source.Pieces;
    stop.Pallets = source.Pallets;
    stop.Temperature = source.Temperature;
    stop.TemperatureUnit = source.TemperatureUnit;
    stop.ScheduledDate = source.ScheduledDate;
    stop.ScheduledTime = source.ScheduledTime;
    stop.ScheduledDate2 = source.ScheduledDate2;
    stop.ScheduledTime2 = source.ScheduledTime2;
    stop.IsWindow = source.IsWindow;
    stop.ArrivedAt = source.ArrivedAt;
    stop.PickedUpAt = source.PickedUpAt;
    stop.DeliveredAt = source.DeliveredAt;
    stop.DepartedAt = source.DepartedAt;
  }
}
