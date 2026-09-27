namespace Domain.Entities.Dispatch;

public sealed class DispatchSourceLink : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string Provider { get; set; } = "";
  public string ExternalId { get; set; } = "";
  public string DisplayName { get; set; } = "";
  public string AssignmentProposalJson { get; set; } = "";
  public string AssignmentSignature { get; set; } = "";
  public string? ExecutionReviewReason { get; set; }

  // The read ticket of the import pass that last wrote this load
  // (DispatchImportRead): a pass holding a smaller one does not write it.
  public long ReadTicket { get; set; }
  public Guid DispatchId { get; set; }
  public Dispatch Dispatch { get; set; } = null!;
}
