namespace Domain.Entities.Dispatch;

public sealed class DispatchNumberCounter : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public const string LoadNumbers = "loads";
  public string Id { get; set; } = LoadNumbers;
  public long NextNumber { get; set; } = 1;
}
