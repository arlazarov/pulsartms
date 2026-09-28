namespace Domain.Entities.Fuel;

// A discount message the import could not read (audit F20): it was not
// imported, and its prices are missing until the cause is fixed while the
// message is still in the mailbox window. Removed when it is imported.
public class FuelImportSkip : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string GmailMessageId { get; set; } = string.Empty;

  public string Reason { get; set; } = string.Empty;

  public DateTime SkippedAt { get; set; }
}
