namespace Application.Features.Fuel.Models;

public class FuelDiscountImportData
{
  public string MessageId { get; set; } = string.Empty;
  public string AttachmentName { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;
  public DateOnly EffectiveDate { get; set; }
  public DateOnly? EffectiveTo { get; set; }
  public List<FuelDiscountImportRow> Rows { get; set; } = [];

  // The attachment could not be read: nothing of its message is imported,
  // and the other messages still are.
  public bool Unreadable { get; set; }
}
