using Application.Features.Routing.Interfaces;

namespace Application.Features.Routing.Background;

public sealed class SourceRoadDemand(ISourceRoadStore store, TimeProvider clock)
{
  public Task RequestAsync(
    Guid dispatchId,
    string identity,
    int priority,
    CancellationToken ct
  ) =>
    store.DemandAsync(
      dispatchId,
      identity,
      priority,
      clock.GetUtcNow().UtcDateTime,
      ct
    );
}
