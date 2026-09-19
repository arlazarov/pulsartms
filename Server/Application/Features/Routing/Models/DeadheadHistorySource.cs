using System.Collections.Immutable;

namespace Application.Features.Routing.Models;

public sealed record DeadheadHistorySource(
  RouteWorkSnapshot Current,
  ImmutableArray<RouteWorkSnapshot> Predecessors,
  bool HasUnknownStart
);
