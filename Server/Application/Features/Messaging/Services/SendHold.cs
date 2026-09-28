using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Messaging.Services;

// This binary sends nothing to the provider while a binary from before
// audit F27 still runs (IPreviousBinary), so no status of a message it
// sends can land on the previous binary and be dropped (the release
// overlap, 90f6267a). Queued replies wait in the outbox; a direct send is
// refused before anything is recorded, to be tried again. Bound: the
// previous binary's stop releases the lease, a dead one's runs out within
// three minutes; asked again every Recheck while held. Once seen gone, this
// process never holds again - the previous binary returns only by a
// rollback, which this does not guard.
public sealed class SendHold(IServiceScopeFactory scopes, TimeProvider clock)
{
  public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);
  private readonly object gate = new();
  private bool released;
  private DateTimeOffset checkedAt = DateTimeOffset.MinValue;

  public async Task<bool> HeldAsync(CancellationToken ct)
  {
    var now = clock.GetUtcNow();
    lock (gate)
    {
      if (released)
        return false;
      if (now - checkedAt < Recheck)
        return true;
    }
    bool runs;
    await using (var scope = scopes.CreateAsyncScope())
      runs = await scope
        .ServiceProvider.GetRequiredService<IPreviousBinary>()
        .RunsAsync(ct);
    lock (gate)
    {
      checkedAt = now;
      released |= !runs;
      return !released;
    }
  }
}
