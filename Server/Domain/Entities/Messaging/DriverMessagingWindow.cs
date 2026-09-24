namespace Domain.Entities.Messaging;

// When a number last wrote to the carrier's messaging number, without
// saying which number. Kept only for revisions released before
// 2026-09-24, which read it to open the fuel plan's 24-hour window, so a
// rolling cutover leaves them working. Messaging still writes it and never
// reads it: a row that names no business number opens no window. Dropping
// the table is a later, explicit cleanup once no running revision reads
// it.
public sealed class DriverMessagingWindow : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public string Channel { get; set; } = "";
  public string Phone { get; set; } = "";
  public DateTime LastInboundAt { get; set; }
}
