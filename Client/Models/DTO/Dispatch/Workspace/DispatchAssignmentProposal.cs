namespace Client.Models.DTO.Dispatch.Workspace;

public sealed record DispatchResourceProposal(
  string Truck,
  string Driver,
  string CoDriver,
  string Trailer,
  Guid? TruckId,
  Guid? DriverId,
  Guid? CoDriverId,
  Guid? TrailerId
);

public sealed record DispatchAssignmentVisit(
  int Sequence,
  string Job,
  string Name,
  DispatchResourceProposal Resources
);

public sealed class DispatchAssignmentProposal
{
  public DispatchResourceProposal Header { get; init; } =
    new("", "", "", "", null, null, null, null);
  public IReadOnlyList<DispatchAssignmentVisit> Visits { get; init; } = [];
}

public sealed record DispatchAcceptedAssignment(
  Guid ExecutionLegId,
  long Revision,
  string Status,
  string From,
  string Through,
  DispatchResourceProposal Resources
)
{
  public Guid FromStopId { get; init; }
  public Guid ThroughStopId { get; init; }
}
