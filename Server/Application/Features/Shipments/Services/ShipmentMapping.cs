using System.Text.Json;
using Application.Features.Shipments.Models;
using Commodity = Domain.Entities.Shipments.ShipmentCommodity;
using Entity = Domain.Entities.Shipments.Shipment;

namespace Application.Features.Shipments.Services;

public static class ShipmentMapping
{
  public static void Apply(Entity target, Shipment source)
  {
    target.BillOfLading = source.BillOfLading;
    target.PickupStopId = source.PickupStopId;
    target.DeliveryStopId = source.DeliveryStopId;
    target.Shipper.Name = source.Shipper.Name;
    target.Shipper.AddressLine1 = source.Shipper.AddressLine1;
    target.Shipper.AddressLine2 = source.Shipper.AddressLine2;
    target.Shipper.City = source.Shipper.City;
    target.Shipper.Region = source.Shipper.Region;
    target.Shipper.Country = source.Shipper.Country;
    target.Shipper.PostalCode = source.Shipper.PostalCode;
    target.Shipper.ContactName = source.Shipper.ContactName;
    target.Shipper.Phone = source.Shipper.Phone;
    target.Shipper.Email = source.Shipper.Email;
    target.Consignee.Name = source.Consignee.Name;
    target.Consignee.AddressLine1 = source.Consignee.AddressLine1;
    target.Consignee.AddressLine2 = source.Consignee.AddressLine2;
    target.Consignee.City = source.Consignee.City;
    target.Consignee.Region = source.Consignee.Region;
    target.Consignee.Country = source.Consignee.Country;
    target.Consignee.PostalCode = source.Consignee.PostalCode;
    target.Consignee.ContactName = source.Consignee.ContactName;
    target.Consignee.Phone = source.Consignee.Phone;
    target.Consignee.Email = source.Consignee.Email;
    var retained = source.Commodities.Select(x => x.Id).ToHashSet();
    target.Commodities.RemoveAll(x => !retained.Contains(x.Id));
    var position = 0;
    foreach (var input in source.Commodities)
    {
      var row = target.Commodities.SingleOrDefault(x => x.Id == input.Id);
      if (row is null)
      {
        row = new Commodity { Id = input.Id };
        target.Commodities.Add(row);
      }
      row.Position = position++;
      row.Description = input.Description;
      row.PackageType = input.PackageType;
      row.WeightUnit = input.WeightUnit;
      row.Marks = input.Marks;
      row.Classification = input.Classification;
      row.OriginCountry = input.OriginCountry;
      row.Quantity = input.Quantity;
      row.Weight = input.Weight;
    }
  }

  public static Shipment Project(Entity row)
  {
    var result = JsonSerializer.Deserialize<Shipment>(
      JsonSerializer.Serialize(row)
    )!;
    var positions = row.Commodities.ToDictionary(x => x.Id, x => x.Position);
    result.Commodities = result
      .Commodities.OrderBy(x => positions[x.Id])
      .ToList();
    return result;
  }
}
