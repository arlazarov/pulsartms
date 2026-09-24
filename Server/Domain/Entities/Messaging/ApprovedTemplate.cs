namespace Domain.Entities.Messaging;

// A message template the provider approved for one of the carrier's
// business numbers, as an administrator recorded it. Only these can be sent
// outside a driver's 24-hour window, and only from that number: a template
// approved for one carrier or number is not another's. Text shows it with
// {{1}}, {{2}} for its parameters, as the driver will read it.
public sealed class ApprovedTemplate : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public string Channel { get; set; } = "";
  public string BusinessNumberId { get; set; } = "";
  public string Name { get; set; } = "";
  public string Language { get; set; } = "";
  public int Parameters { get; set; }
  public string Text { get; set; } = "";
  public DateTime CreatedAt { get; set; }
  public Guid? CreatedBy { get; set; }
}
