using System.Collections.Immutable;

namespace Domain.Models.Routing;

public sealed record DeadheadHistorySource(
  RouteWorkSnapshot Current,
  ImmutableArray<RouteWorkSnapshot> Predecessors,
  bool HasUnknownStart
);
