using Domain.Models.Execution;

namespace Domain.Rules.Routing;

public sealed record CurrentWorkChoice(
  TruckWorkSegment? Current,
  IReadOnlyList<TruckWorkSegment> Passed
);
