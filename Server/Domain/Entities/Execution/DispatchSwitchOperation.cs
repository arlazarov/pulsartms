namespace Domain.Entities.Execution;

public sealed class DispatchSwitchOperation : BaseEntity
{
  public string Status { get; set; } = "planned";
  public long Revision { get; set; }
  public string SiteName { get; set; } = "";
  public decimal Latitude { get; set; }
  public decimal Longitude { get; set; }
  public DateTime? PlannedAt { get; set; }
  public DateTime? CompletedAt { get; set; }
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
  public Guid? CompletedBy { get; set; }
  public Guid IdempotencyKey { get; set; }
  public string RequestHash { get; set; } = "";
  public List<SwitchParticipant> Participants { get; set; } = [];
}
