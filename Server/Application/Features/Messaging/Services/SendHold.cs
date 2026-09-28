using Application.Features.Messaging.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Messaging.Services;

// A deployed revision sends nothing to the messaging provider until an
// administrator releases it (SendRelease), having seen the platform drain
// the revision before it: while that one can still receive webhooks, a
// status of a message this one sends could land there and, in a binary
// from before audit F27, be dropped (the release overlap, 90f6267a).
// Nothing inferred releases it - not a free lease, not a quiet loop: a
// running binary can hold no lease and still answer webhooks. Queued
// replies wait in the outbox; a direct send is refused before anything is
// recorded, to be tried again. Where no overlap can happen
// (RequireRelease false) nothing is held.
//
// Asked of the database at most every Recheck, one read shared by
// concurrent callers; a recorded release is kept for the process's life,
// as the record is. A process whose revision has no valid name is never
// released. A failed read holds too, and is not retried before Recheck.
public sealed class SendHold(
  IServiceScopeFactory scopes,
  IDeploymentRevision revision,
  IOptions<SendHoldOptions> options,
  TimeProvider clock,
  ILogger<SendHold> logger
)
{
  public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);
  private readonly object gate = new();
  private bool released;
  private bool failing;
  private DateTimeOffset checkedAt = DateTimeOffset.MinValue;
  private Task<bool>? reading;

  public string? Revision => revision.Name;
  public bool Required => options.Value.RequireRelease;

  public async Task<bool> HeldAsync(CancellationToken ct)
  {
    if (!Required)
      return false;
    // A revision the platform did not name cannot be released: its sends
    // stay held, and no other process's release can apply to it.
    if (revision.Name is null)
      return true;
    Task<bool> read;
    lock (gate)
    {
      if (released)
        return false;
      if (clock.GetUtcNow() - checkedAt < Recheck)
        return true;
      read = reading ??= Task.Run(ReadAsync);
    }
    return !await read.WaitAsync(ct);
  }

  // Whether the revision is released; shared by the callers that asked
  // while it ran, so it is not cancelled by one of them. A read that fails
  // holds, like one that finds nothing, and counts as the check: the next
  // is Recheck later, however many callers ask meanwhile. The failure is
  // logged once, when reads start failing, not on every retry.
  private async Task<bool> ReadAsync()
  {
    var found = false;
    Exception? failure = null;
    try
    {
      await using var scope = scopes.CreateAsyncScope();
      found = await scope
        .ServiceProvider.GetRequiredService<IAppDbContext>()
        .SendReleases.AsNoTracking()
        .AnyAsync(x => x.Revision == revision.Name!);
    }
    catch (Exception ex)
    {
      failure = ex;
    }
    bool first;
    lock (gate)
    {
      released |= found;
      checkedAt = clock.GetUtcNow();
      first = failure is not null && !failing;
      failing = failure is not null;
      reading = null;
    }
    if (first)
      logger.LogWarning(
        failure,
        "The send release could not be read; sends stay held and it is "
          + "read again every {Recheck}",
        Recheck
      );
    return found;
  }
}
