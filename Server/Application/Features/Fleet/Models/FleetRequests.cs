namespace Application.Features.Fleet.Models;

// What callers send to change a driver's contact or a fleet resource.

// A field taken from the source follows later imports again; a local field
// left empty is cleared here and stays cleared through imports.
public sealed record DriverContactUpdate(
  long Revision,
  string? Phone,
  bool PhoneFromSource,
  string? Email,
  bool EmailFromSource,
  string? WhatsAppPhone
);

public sealed record FleetConfigurationUpdate(
  long Revision,
  string Name,
  string Vin,
  string FuelCard,
  bool IsActive,
  bool UseImported = false
);
