using System.Collections.Immutable;

namespace Domain.Models.Routing;

public sealed record FuelHistoryInput(
  RouteWorkSnapshot Current,
  string InputSignature
);

public sealed record FuelHistoryBatch(ImmutableArray<FuelHistoryInput> Inputs);

public sealed record FuelHistoryDependencies(
  int Version,
  ImmutableArray<FuelHistoryBatch> Batches
)
{
  public const int CurrentVersion = 1;

  public static FuelHistoryDependencies Capture(
    IReadOnlyCollection<DeadheadHistoryBatch> batches
  ) =>
    new(
      CurrentVersion,
      batches
        .Select(batch => new FuelHistoryBatch(
          batch
            .Snapshots.Select(x => new FuelHistoryInput(
              x.Current,
              x.InputSignature
            ))
            .ToImmutableArray()
        ))
        .ToImmutableArray()
    );
}
