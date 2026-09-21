using Domain.Entities;

namespace Domain.Entities.Dispatch;

public sealed class PlanningRefreshRequest
{
  // Which carrier this piece of work is for. The row is the
  // server's, not the carrier's - a worker claims whatever is next,
  // whoever it is for, and then runs the pass as them.
  public Guid CompanyId { get; set; }

  public string Id { get; set; } = "";
  public Guid DispatchId { get; set; }
  public Guid? ExecutionLegId { get; set; }
  public long AssignmentRevision { get; set; }
  public string InputSignature { get; set; } = "";
  public long RequestedVersion { get; set; }
  public long CompletedVersion { get; set; }
  public DateTime RequestedAt { get; set; }
  public DateTime AvailableAt { get; set; }
  public Guid? LeaseId { get; set; }
  public DateTime? LeaseUntil { get; set; }
  public int Attempts { get; set; }
}
