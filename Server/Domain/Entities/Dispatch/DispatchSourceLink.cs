namespace Domain.Entities.Dispatch;

public sealed class DispatchSourceLink
{
  public string Provider { get; set; } = "";
  public string ExternalId { get; set; } = "";
  public string DisplayName { get; set; } = "";
  public string AssignmentProposalJson { get; set; } = "";
  public string AssignmentSignature { get; set; } = "";
  public string? ExecutionReviewReason { get; set; }
  public Guid DispatchId { get; set; }
  public Dispatch Dispatch { get; set; } = null!;
}
