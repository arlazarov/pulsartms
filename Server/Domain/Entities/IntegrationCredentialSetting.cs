namespace Domain.Entities;

public sealed class IntegrationCredentialSetting : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string Provider { get; set; } = string.Empty;
  public string? ProtectedValues { get; set; }
  public long Revision { get; set; }
  public DateTime UpdatedAt { get; set; }
}
