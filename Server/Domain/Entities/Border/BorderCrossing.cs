using Domain.Entities.Shipments;

namespace Domain.Entities.Border;

public sealed class BorderEquipment
{
  public Guid Id { get; set; }
  public Guid? TruckId { get; set; }
  public Guid? TrailerId { get; set; }
  public string Kind { get; set; } = "";
  public string UnitNumber { get; set; } = "";
  public string Vin { get; set; } = "";
  public string EquipmentType { get; set; } = "";
  public string PlateNumber { get; set; } = "";
  public string PlateRegion { get; set; } = "";
  public string PlateCountry { get; set; } = "";
  public string ContainerNumber { get; set; } = "";
  public string SealNumbers { get; set; } = "";
}

public sealed class BorderShipment
{
  public Guid Id { get; set; }
  public Guid ShipmentId { get; set; }
  public long ShipmentRevision { get; set; }
  public string Procedure { get; set; } = "";
  public string ParsNumber { get; set; } = "";
  public string PapsNumber { get; set; } = "";
  public string ReleaseOffice { get; set; } = "";
  public string LoadingCity { get; set; } = "";
  public string LoadingRegion { get; set; } = "";
  public string LoadingCountry { get; set; } = "";
  public bool Consolidated { get; set; }
  public Guid? EquipmentId { get; set; }
  public ShipmentParty Importer { get; set; } = new();
  public ShipmentParty CustomsBroker { get; set; } = new();
  public string ShipmentSnapshotJson { get; set; } = "{}";
}

public sealed class BorderCrew
{
  public Guid Id { get; set; }
  public Guid? DriverId { get; set; }
  public string Role { get; set; } = "";
  public string DisplayName { get; set; } = "";
  public string ProtectedDetails { get; set; } = "";
}

public sealed class BorderCrossing : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid Id { get; set; }
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
  public string Reference { get; set; } = "";
  public string DestinationCountry { get; set; } = "";
  public string PortOfEntry { get; set; } = "";
  public DateOnly? ArrivalDate { get; set; }
  public TimeOnly? ArrivalTime { get; set; }
  public string ArrivalTimeZone { get; set; } = "";
  public string CarrierName { get; set; } = "";
  public string Scac { get; set; } = "";
  public string CanadianCarrierCode { get; set; } = "";
  public bool EmptyConveyance { get; set; }
  public Guid? SourceLegId { get; set; }
  public Guid? SourceStopId { get; set; }
  public long? SourceRevision { get; set; }
  public ShipmentParty CarrierAddress { get; set; } = new();
  public Guid UpdatedBy { get; set; }
  public List<BorderShipment> Shipments { get; set; } = [];
  public List<BorderCrew> Crew { get; set; } = [];
  public List<BorderEquipment> Equipment { get; set; } = [];
}

public sealed class BorderSaveReceipt : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid Id { get; set; }
  public Guid ActorId { get; set; }
  public Guid CrossingId { get; set; }
  public string RequestHash { get; set; } = "";
  public string ProtectedResponse { get; set; } = "";
}
