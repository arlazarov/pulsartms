namespace Domain.Entities.Shipments;

public sealed class ShipmentParty
{
  public string Name { get; set; } = "";
  public string AddressLine1 { get; set; } = "";
  public string AddressLine2 { get; set; } = "";
  public string City { get; set; } = "";
  public string Region { get; set; } = "";
  public string Country { get; set; } = "";
  public string PostalCode { get; set; } = "";
  public string ContactName { get; set; } = "";
  public string Phone { get; set; } = "";
  public string Email { get; set; } = "";
}

public sealed class ShipmentCommodity
{
  public int Position { get; set; }
  public Guid Id { get; set; }
  public string Description { get; set; } = "";
  public string PackageType { get; set; } = "";
  public string WeightUnit { get; set; } = "";
  public string Marks { get; set; } = "";
  public string Classification { get; set; } = "";
  public string OriginCountry { get; set; } = "";
  public int? Quantity { get; set; }
  public decimal? Weight { get; set; }
}

public sealed class Shipment : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid LoadId { get; set; }
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
  public Guid UpdatedBy { get; set; }
  public string BillOfLading { get; set; } = "";
  public Guid? PickupStopId { get; set; }
  public Guid? DeliveryStopId { get; set; }
  public ShipmentParty Shipper { get; set; } = new();
  public ShipmentParty Consignee { get; set; } = new();
  public List<ShipmentCommodity> Commodities { get; set; } = [];
}
