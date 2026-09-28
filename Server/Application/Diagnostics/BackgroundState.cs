namespace Application.Diagnostics;

public sealed class BackgroundState : IBackgroundState
{
  public IReadOnlyList<string> Stalled(DateTimeOffset now) =>
    BackgroundHeartbeat.Stalled(now);

  public IReadOnlyList<string> StaleProgress(DateTimeOffset now) =>
    [
      .. BackgroundProgress
        .Read(now)
        .Where(x => x.Stale)
        .Select(x => x.Operation),
    ];
}
