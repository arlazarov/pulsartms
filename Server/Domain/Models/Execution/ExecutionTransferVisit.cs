namespace Domain.Models.Execution;

public sealed record ExecutionTransferVisit
{
  public Guid Id { get; init; }
  public Guid TripId { get; init; }
  public Guid? SourceDispatchStopId { get; init; }
  public string Operation { get; init; } = "";
  public string SiteName { get; init; } = "";
  public decimal? Latitude { get; init; }
  public decimal? Longitude { get; init; }
  public DateTime? PlannedAt { get; init; }
  public DateTime? ActualAt { get; init; }
  public Guid? ConfirmedBy { get; init; }
  public long Revision { get; init; }
}
