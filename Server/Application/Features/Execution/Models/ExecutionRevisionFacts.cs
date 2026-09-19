using System.Collections.Immutable;
using System.Text.Json;
using Domain.Entities.Execution;

namespace Application.Features.Execution.Models;

public sealed record ExecutionRevisionLoad(
  Guid Id,
  Guid DispatchId,
  int Sequence,
  Guid StartVisitId,
  Guid EndVisitId
);

public sealed record ExecutionRevisionFacts(
  Guid TripId,
  Guid TruckId,
  Guid? DriverId,
  Guid? CoDriverId,
  Guid? TrailerId,
  string Status,
  Guid? StartSwitchId,
  Guid? EndSwitchId,
  DateTime? StartedAt,
  DateTime? CompletedAt,
  string SourceSignature,
  string? SourceReviewReason,
  ImmutableArray<ExecutionLegStop> Stops,
  ImmutableArray<ExecutionRevisionLoad> Loads,
  ImmutableArray<ExecutionTransferVisit> Transfers
)
{
  public string? ActorName { get; init; }

  public string SourceAssignmentSignature { get; init; } = "";

  public static ExecutionRevisionFacts Read(ExecutionLegRevision revision)
  {
    if (revision.SchemaVersion != 1)
      throw new NotSupportedException("Unknown execution history version.");
    return JsonSerializer.Deserialize<ExecutionRevisionFacts>(
        revision.SnapshotJson
      ) ?? throw new JsonException("Missing execution history facts.");
  }
}
