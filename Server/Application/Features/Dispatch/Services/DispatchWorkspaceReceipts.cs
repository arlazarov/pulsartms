using Application.Features.Dispatch.Models;

namespace Application.Features.Dispatch.Services;

// A workspace save that arrives with a retry identity already recorded.
// The same request (by its hash) from the same person, on the same load,
// gets the recorded answer again; any other use of that identity is
// refused, in the words and status each command chooses. A new load has no
// id yet, so its creation passes none.
public static class DispatchWorkspaceReceipts
{
  // Answer is null when the identity belongs to another request.
  public sealed record Replay(DispatchWorkspaceResponse? Answer);

  public static async Task<Replay?> FindAsync(
    IAppDbContext db,
    Guid idempotencyKey,
    string requestHash,
    Guid actor,
    Guid? dispatchId,
    CancellationToken ct
  )
  {
    var receipt = await db
      .DispatchWorkspaceRevisions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
    if (receipt is null)
      return null;
    return new(
      receipt.RequestHash == requestHash
      && receipt.RecordedBy == actor
      && (dispatchId is null || receipt.DispatchId == dispatchId)
        ? DispatchWorkspaceData.Read<DispatchWorkspaceResponse>(
          receipt.SnapshotJson
        )
        : null
    );
  }
}
