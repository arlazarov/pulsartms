namespace Domain.Entities.Dispatch;

public class Customer : BaseEntity
{
  public string Name { get; set; } = string.Empty;
  public string NormalizedName { get; set; } = string.Empty;
  public List<Dispatch> Dispatches { get; set; } = [];
}
