namespace Domain.Entities.Fleet;

public interface IFleetConfiguration
{
  Guid Id { get; set; }
  bool IsActive { get; set; }
  bool? ImportedIsActive { get; set; }
  bool IsLocallyConfigured { get; set; }
  long ConfigurationRevision { get; set; }
  DateTime? ConfiguredAt { get; set; }
  string? ConfiguredBy { get; set; }
}
