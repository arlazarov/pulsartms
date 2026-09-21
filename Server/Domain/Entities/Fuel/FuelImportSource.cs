namespace Domain.Entities.Fuel;

public class FuelImportSource : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string GmailMessageId { get; set; } = string.Empty;

  public string AttachmentName { get; set; } = string.Empty;

  public DateTime ImportedAt { get; set; }
}
