using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Caching;
using Application.Features.Routing.Services.Addresses;
using Domain.Entities.Dispatch;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Background;

public static class RoutePreparationInputs
{
  public static Guid? Truck(Load load, Guid? known = null) => load.TruckId
    ?? (load.Stops.Where(x => x.TruckId.HasValue).Select(x => x.TruckId).Distinct().Take(2).ToArray() is [var truck] ? truck : known);

  public static string Signature(Load load, Guid? truckId, long connectionVersion, ReadCache reads, DateTime now,
    long? profileGeneration = null) =>
    Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
      load.Id, load.Status, load.TruckId, load.TruckNumber, load.ShipDate, load.DeliveryDate,
      load.Price, load.Currency, load.LoadedMiles, connectionVersion,
      Profile = profileGeneration ?? reads.Generation($"profile:{truckId ?? Guid.Empty}"),
      Stops = load.Stops.OrderBy(x => x.Sequence).Select(x => new
      {
        x.Id, x.Sequence, x.TruckId, x.Job, x.Address, x.City, x.Province, x.Country, x.ZipCode,
        x.Latitude, x.Longitude, x.SourceAddressJson, x.AddressVerifiedAt, x.AddressRetryAfter,
        Verified = StopLocation.VerifiedPoint(x, now) is not null,
        x.ScheduledDate, x.ScheduledTime, x.DeliveredAt, x.DepartedAt, x.PickedUpAt
      })
    })));

  public static readonly Expression<Func<Load, Load>> Projection = load => new Load
  {
    Id = load.Id, TruckId = load.TruckId, TruckNumber = load.TruckNumber, Status = load.Status,
    ShipDate = load.ShipDate, DeliveryDate = load.DeliveryDate, Price = load.Price,
    Currency = load.Currency, LoadedMiles = load.LoadedMiles,
    Stops = load.Stops.Select(stop => new DispatchStop
    {
      Id = stop.Id, Sequence = stop.Sequence, TruckId = stop.TruckId, Job = stop.Job,
      Address = stop.Address, City = stop.City, Province = stop.Province, Country = stop.Country, ZipCode = stop.ZipCode,
      Latitude = stop.Latitude, Longitude = stop.Longitude, SourceAddressJson = stop.SourceAddressJson,
      AddressVerifiedAt = stop.AddressVerifiedAt, AddressRetryAfter = stop.AddressRetryAfter,
      ScheduledDate = stop.ScheduledDate, ScheduledTime = stop.ScheduledTime,
      DeliveredAt = stop.DeliveredAt, DepartedAt = stop.DepartedAt, PickedUpAt = stop.PickedUpAt
    }).ToList()
  };
}
