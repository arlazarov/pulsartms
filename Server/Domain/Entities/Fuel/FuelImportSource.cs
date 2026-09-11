namespace Domain.Entities.Fuel;

public class FuelImportSource : BaseEntity
{
  public string GmailMessageId { get; set; } = string.Empty;

  public string AttachmentName { get; set; } = string.Empty;

  public DateTime ImportedAt { get; set; }
}
