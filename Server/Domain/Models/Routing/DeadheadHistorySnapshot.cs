using System.Collections.Immutable;

namespace Domain.Models.Routing;

public sealed record DeadheadHistoryBatch(
  ImmutableArray<DeadheadHistorySnapshot> Snapshots
);

public sealed record DeadheadHistorySnapshot(
  RouteWorkSnapshot Current,
  ImmutableArray<RouteWorkSnapshot> Predecessors,
  bool HasUnknownStart,
  string InputSignature
);
