namespace Application.Features.Dispatch.Models;

public sealed class StopCorrectionRequest
{
  public long ExpectedRevision { get; set; }
  public string SourceFingerprint { get; set; } = "";
  public Guid IdempotencyKey { get; set; }
  public string Completion { get; set; } = "keep";
  public DateTimeOffset? CompletedAt { get; set; }
  public bool ChangeAssignment { get; set; }
  public string AssignmentScope { get; set; } = "current";
  public Guid? FromStopId { get; set; }
  public Guid? ToStopId { get; set; }
  public bool? ChangeTruck { get; set; }
  public bool? ChangeTrailer { get; set; }
  public bool? ChangeDriver { get; set; }
  public bool? ChangeCoDriver { get; set; }
  public Guid? TruckId { get; set; }
  public Guid? TrailerId { get; set; }
  public Guid? DriverId { get; set; }
  public Guid? CoDriverId { get; set; }
  public string Reason { get; set; } = "";
}
