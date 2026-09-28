namespace Application.Interfaces;

// What this instance's background work reports, for the health checks
// outside Application. Liveness asks for stalled loops (a restart);
// readiness for stale progress (a report only).
public interface IBackgroundState
{
  IReadOnlyList<string> Stalled(DateTimeOffset now);

  IReadOnlyList<string> StaleProgress(DateTimeOffset now);
}
