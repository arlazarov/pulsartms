namespace Domain.Entities.Fleet;

public class SynchronizationCheckpoint : BaseEntity
{
  public string Owner { get; set; } = "";
  public DateTime LeaseUntil { get; set; }
  public DateTime UpdatedAt { get; set; }
  public string StateJson { get; set; } = "{}";
}
