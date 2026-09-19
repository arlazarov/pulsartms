using Client.Models.DTO.Shipments;

namespace Client.Models.DTO.Border;

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
  public Shipment? Snapshot { get; set; }
  public ShipmentParty Importer { get; set; } = new();
  public ShipmentParty CustomsBroker { get; set; } = new();
}

public sealed class BorderCrew
{
  public Guid Id { get; set; }
  public Guid? DriverId { get; set; }
  public string Role { get; set; } = "";
  public string DisplayName { get; set; } = "";
  public BorderPerson Details { get; set; } = new();
}

public sealed class BorderCrossing
{
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
  public List<BorderShipment> Shipments { get; set; } = [];
  public List<BorderCrew> Crew { get; set; } = [];
  public List<BorderEquipment> Equipment { get; set; } = [];
  public ShipmentParty CarrierAddress { get; set; } = new();
}

public sealed class BorderPerson
{
  public string FirstName { get; set; } = "";
  public string MiddleName { get; set; } = "";
  public string LastName { get; set; } = "";
  public string CitizenshipCountry { get; set; } = "";
  public string Sex { get; set; } = "";
  public string FastCardNumber { get; set; } = "";
  public DateOnly? DateOfBirth { get; set; }
  public List<BorderTravelDocument> Documents { get; set; } = [];
}

public sealed class BorderTravelDocument
{
  public Guid Id { get; set; }
  public string Type { get; set; } = "";
  public string Number { get; set; } = "";
  public string IssuingCountry { get; set; } = "";
  public string IssuingRegion { get; set; } = "";
  public DateOnly? ExpiresOn { get; set; }
}

public sealed record SaveBorderCrossing(
  Guid RequestId,
  long ExpectedRevision,
  BorderCrossing Crossing
);

public sealed record BorderSummary(
  Guid Id,
  string Reference,
  string DestinationCountry,
  string PortOfEntry,
  long Revision
);

public sealed record BorderShipmentOption(
  Guid Id,
  Guid LoadId,
  int LoadNumber,
  string Broker,
  string BillOfLading,
  long Revision
);

public sealed record BorderAssignmentOption(
  Guid LegId,
  Guid StopId,
  long Revision,
  string Label,
  Guid TruckId,
  Guid? TrailerId,
  Guid? DriverId,
  Guid? CoDriverId
);

public sealed record BorderPort(string Country, string Code, string Name);
