namespace Domain.Entities.Dispatch;

public sealed class SourceRoadRequest
{
  public Guid DispatchId { get; set; }
  public Guid? TruckId { get; set; }
  public string InputSignature { get; set; } = "";
  public string DemandIdentity { get; set; } = "";
  public bool Explicit { get; set; }
  public int Priority { get; set; }
  public long RequestedVersion { get; set; }
  public long CompletedVersion { get; set; }
  public DateTime RequestedAt { get; set; }
  public DateTime AvailableAt { get; set; }
  public Guid? LeaseId { get; set; }
  public DateTime? LeaseUntil { get; set; }
  public int Attempts { get; set; }
}
