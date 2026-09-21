namespace Domain.Entities.Mileage;

public sealed class MovementDistanceEvidence : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid MovementId { get; init; }
  public long Revision { get; init; }
  public string Basis { get; init; } = "planned";
  public decimal Miles { get; init; }
  public string Source { get; init; } = "";
  public string SourceReference { get; init; } = "";
  public string Reason { get; init; } = "";
  public DateTime ObservedAt { get; init; }
  public DateTime? StartedAt { get; init; }
  public DateTime? EndedAt { get; init; }
  public decimal? StartOdometerMeters { get; init; }
  public decimal? EndOdometerMeters { get; init; }
  public DateTime RecordedAt { get; init; }
  public Guid RecordedBy { get; init; }
}
