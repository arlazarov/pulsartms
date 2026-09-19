namespace Domain.Entities.Execution;

public sealed class Trip : BaseEntity
{
  public string Name { get; set; } = "";
  public string Status { get; set; } = "planned";
  public long Revision { get; set; }
  public DateTime RecordedAt { get; set; }
  public Guid? RecordedBy { get; set; }
}
