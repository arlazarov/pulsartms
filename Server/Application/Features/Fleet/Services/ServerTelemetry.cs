using Application.Features.Fleet.Models;

namespace Application.Features.Fleet.Services;

public sealed class ServerTelemetry
{
  private readonly string instance = Guid.NewGuid().ToString("N")[..8];
  private long publications;
  private FleetLocationsResponse? value;
  private TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public FleetLocationsResponse? Current => Volatile.Read(ref value);

  public void Set(FleetLocationsResponse snapshot)
  {
    // The instance prefix keeps revisions distinct across restarts and instances.
    snapshot.Revision = $"{instance}-{Interlocked.Increment(ref publications)}";
    Volatile.Write(ref value, snapshot);
    Interlocked.Exchange(ref published, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
  }

  // Held browser polls wake on the next publication; the signal is read before the revision so a
  // publication between the two cannot be missed.
  public async Task<bool> WaitForChangeAsync(string? knownRevision, TimeSpan timeout, CancellationToken ct)
  {
    var signal = Volatile.Read(ref published).Task;
    if (Current?.Revision != knownRevision) return true;
    if (timeout <= TimeSpan.Zero) return false;
    using var timer = new CancellationTokenSource(timeout);
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
    try { await signal.WaitAsync(linked.Token); return true; }
    catch (OperationCanceledException) when (timer.IsCancellationRequested && !ct.IsCancellationRequested) { return false; }
  }
}
