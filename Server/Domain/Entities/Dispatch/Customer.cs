namespace Domain.Entities.Dispatch;

public class Customer : BaseEntity
{
  public string Name { get; set; } = string.Empty;
  public string NormalizedName { get; set; } = string.Empty;
  public long ProfileRevision { get; set; }
  public string ProfileJson { get; set; } = "{}";
  public List<Dispatch> Dispatches { get; set; } = [];
}
