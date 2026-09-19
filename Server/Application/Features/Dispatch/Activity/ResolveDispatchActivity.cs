using Application.Models;

namespace Application.Features.Dispatch.Activity;

public sealed record ResolveDispatchActivityCommand(
  Guid DispatchId,
  Guid EntryId,
  ResolveDispatchActivityUpdate Update
) : IRequest<RequestResponse<DispatchActivityItem>>;

public sealed class ResolveDispatchActivityHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    ResolveDispatchActivityCommand,
    RequestResponse<DispatchActivityItem>
  >
{
  public async Task<RequestResponse<DispatchActivityItem>> Handle(
    ResolveDispatchActivityCommand request,
    CancellationToken ct
  )
  {
    var actor = await DispatchActivityAccess.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return RequestResponse<DispatchActivityItem>.Fail("Forbidden.", 403);
    var update = request.Update;
    if (update is null || update.OperationId == Guid.Empty)
      return RequestResponse<DispatchActivityItem>.Fail("Invalid resolution.");
    var entry = await db.DispatchActivityEntries.SingleOrDefaultAsync(
      x => x.Id == request.EntryId && x.DispatchId == request.DispatchId,
      ct
    );
    if (entry is null)
      return RequestResponse<DispatchActivityItem>.Fail("Note not found.", 404);
    if (
      entry.ResolveOperationId == update.OperationId
      && entry.ResolvedBy == actor.Id
    )
      return RequestResponse<DispatchActivityItem>.Ok(
        DispatchActivityAccess.Item(entry)
      );
    if (
      !entry.NeedsAttention
      || entry.ResolvedAt.HasValue
      || entry.Revision != update.ExpectedRevision
      || entry.Revision == long.MaxValue
    )
      return Conflict();
    var thread = await db.DispatchActivityThreads.SingleAsync(
      x => x.Id == request.DispatchId,
      ct
    );
    if (thread.Revision == long.MaxValue)
      return Conflict();
    thread.Revision++;
    entry.Revision++;
    entry.ResolveOperationId = update.OperationId;
    entry.ResolvedBy = actor.Id;
    entry.ResolvedByName = actor.Name;
    entry.ResolvedAt = clock.GetUtcNow().UtcDateTime;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException exception) when (db.IsWriteConflict(exception))
    {
      db.Entry(entry).State = EntityState.Detached;
      db.Entry(thread).State = EntityState.Detached;
      var saved = await db
        .DispatchActivityEntries.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.Id == request.EntryId && x.DispatchId == request.DispatchId,
          ct
        );
      return
        saved?.ResolveOperationId == update.OperationId
        && saved.ResolvedBy == actor.Id
        ? RequestResponse<DispatchActivityItem>.Ok(
          DispatchActivityAccess.Item(saved)
        )
        : Conflict();
    }
    return RequestResponse<DispatchActivityItem>.Ok(
      DispatchActivityAccess.Item(entry)
    );
  }

  private static RequestResponse<DispatchActivityItem> Conflict() =>
    RequestResponse<DispatchActivityItem>.Fail(
      "This issue changed. Refresh activity before resolving it.",
      409
    );
}
