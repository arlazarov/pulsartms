namespace Domain.Models.Fleet;

public sealed record FleetConfigurationRow(
  Guid Id,
  string Name,
  string Vin,
  bool IsActive,
  bool IsLocallyConfigured,
  long Revision
)
{
  public FleetConfigurationRow()
    : this(Guid.Empty, "", "", false, false, 0) { }
}

public sealed record FleetConfigurationState(
  FleetConfigurationRow Resource,
  string FuelCard,
  string? ImportedName,
  string? ImportedVin,
  bool? ImportedIsActive,
  string[] CurrentAssignments,
  FleetConfigurationDispatch[] ReferencedDispatches,
  DateTime? ConfiguredAt
);

public sealed record FleetConfigurationDispatch(Guid Id, int LoadNumber);

public sealed record FleetConfigurationUpdate(
  long Revision,
  string Name,
  string Vin,
  string FuelCard,
  bool IsActive,
  bool UseImported = false
);
