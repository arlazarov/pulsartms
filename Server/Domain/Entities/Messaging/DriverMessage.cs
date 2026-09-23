namespace Domain.Entities.Messaging;

// One attempt to send a driver a message through a provider.
//
// The row is committed before the provider is called, so an attempt that
// dies half way is found as not known rather than tried again unseen. The
// key names the content and recipient; a second press for the same key
// returns this attempt instead of sending again, and a new attempt is made
// only after a refusal or failure, or when a dispatcher explicitly sends
// again after an unknown outcome. Text is kept so the hand-over can be
// read back; provider errors are kept as numbers only.
public sealed class DriverMessage : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid DriverId { get; set; }
  public Guid TruckId { get; set; }
  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public DateTime PlanCalculatedAt { get; set; }
  public string Channel { get; set; } = "";
  public string Recipient { get; set; } = "";
  public string Text { get; set; } = "";
  public string VisitKeys { get; set; } = "";
  public string IdempotencyKey { get; set; } = "";
  public int Attempt { get; set; }
  public string? ProviderMessageId { get; set; }
  public string Status { get; set; } = "";
  public DateTime StatusAt { get; set; }
  public int? ErrorCode { get; set; }
  public DateTime CreatedAt { get; set; }
  public string? CreatedBy { get; set; }
}
