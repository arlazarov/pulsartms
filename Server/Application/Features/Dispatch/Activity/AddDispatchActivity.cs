using Application.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Activity;

public sealed record AddDispatchActivityCommand(
  Guid DispatchId,
  AddDispatchActivityUpdate Update
) : IRequest<RequestResponse<DispatchActivityItem>>, IChecked
{
  internal static bool ValidKind(string kind) =>
    kind is "driver-called" or "called-driver" or "note";

  public IEnumerable<string> Wrong()
  {
    if (DispatchId == Guid.Empty)
      yield return "Choose a load.";
    if (Update is null)
      yield return "The note to save is missing.";
    else
    {
      if (Update.OperationId == Guid.Empty)
        yield return "Reopen the load before adding a note.";
      if (Update.ExpectedRevision is < 0 or >= long.MaxValue)
        yield return "Reopen the load before adding a note.";
      if (!ValidKind(Update.Kind))
        yield return "Choose a call or a note.";
      if (string.IsNullOrWhiteSpace(Update.Text) || Update.Text.Length > 4000)
        yield return "Write between 1 and 4000 characters.";
    }
  }
}

public sealed class AddDispatchActivityHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    AddDispatchActivityCommand,
    RequestResponse<DispatchActivityItem>
  >
{
  public async Task<RequestResponse<DispatchActivityItem>> Handle(
    AddDispatchActivityCommand request,
    CancellationToken ct
  )
  {
    var actor = await DispatchActivityAccess.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return RequestResponse<DispatchActivityItem>.Fail("Forbidden.", 403);
    var update = request.Update;
    if (
      update is null
      || update.OperationId == Guid.Empty
      || update.ExpectedRevision < 0
      || update.ExpectedRevision == long.MaxValue
      || !AddDispatchActivityCommand.ValidKind(update.Kind)
      || string.IsNullOrWhiteSpace(update.Text)
      || update.Text.Length > 4000
    )
      return RequestResponse<DispatchActivityItem>.Fail("Enter a valid note.");
    var existing = await ReceiptAsync(request, ct);
    if (existing is not null)
      return Replay(existing, update, actor.Id);
    if (!await db.Dispatches.AnyAsync(x => x.Id == request.DispatchId, ct))
      return RequestResponse<DispatchActivityItem>.Fail("Load not found.", 404);
    var stopLabel = update.StopId.HasValue
      ? await StopLabelAsync(request.DispatchId, update.StopId.Value, ct)
      : null;
    if (update.StopId.HasValue && stopLabel is null)
      return RequestResponse<DispatchActivityItem>.Fail(
        "The linked stop is no longer part of this load. Refresh the load.",
        409
      );
    var driverName = update.DriverId.HasValue
      ? await db
        .Drivers.AsNoTracking()
        .Where(x => x.Id == update.DriverId)
        .Select(x => x.Name)
        .SingleOrDefaultAsync(ct)
      : null;
    if (update.DriverId.HasValue && driverName is null)
      return RequestResponse<DispatchActivityItem>.Fail("Driver not found.");
    var thread = await db.DispatchActivityThreads.SingleOrDefaultAsync(
      x => x.Id == request.DispatchId,
      ct
    );
    if ((thread?.Revision ?? 0) != update.ExpectedRevision)
      return Conflict();
    if (thread is null)
    {
      thread = new DispatchActivityThread { Id = request.DispatchId };
      db.DispatchActivityThreads.Add(thread);
    }
    thread.Revision++;
    var entry = new DispatchActivityEntry
    {
      Id = Guid.NewGuid(),
      DispatchId = request.DispatchId,
      CreatedRevision = thread.Revision,
      Revision = 1,
      AddOperationId = update.OperationId,
      Kind = update.Kind,
      Text = update.Text.Trim(),
      StopId = update.StopId,
      StopLabel = stopLabel,
      DriverId = update.DriverId,
      DriverName = driverName,
      ActorId = actor.Id,
      ActorName = actor.Name,
      RecordedAt = clock.GetUtcNow().UtcDateTime,
      NeedsAttention = update.NeedsAttention,
    };
    db.DispatchActivityEntries.Add(entry);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException exception)
    {
      db.Entry(entry).State = EntityState.Detached;
      db.Entry(thread).State = EntityState.Detached;
      existing = await ReceiptAsync(request, ct);
      if (existing is not null)
        return Replay(existing, update, actor.Id);
      var revision =
        await db
          .DispatchActivityThreads.AsNoTracking()
          .Where(x => x.Id == request.DispatchId)
          .Select(x => (long?)x.Revision)
          .SingleOrDefaultAsync(ct) ?? 0;
      if (revision != update.ExpectedRevision || db.IsWriteConflict(exception))
        return Conflict();
      throw;
    }
    return RequestResponse<DispatchActivityItem>.Ok(
      DispatchActivityAccess.Item(entry),
      201
    );
  }

  private async Task<string?> StopLabelAsync(
    Guid dispatchId,
    Guid stopId,
    CancellationToken ct
  )
  {
    var source = await db
      .DispatchStops.AsNoTracking()
      .Where(x => x.DispatchId == dispatchId && x.Id == stopId)
      .Select(x => new
      {
        Job = x.ManualAction ?? x.Job,
        x.Name,
        x.City,
      })
      .SingleOrDefaultAsync(ct);
    if (source is not null)
      return $"{source.Job} · {source.Name} · {source.City}";
    var visit = await db
      .ExecutionLegStops.AsNoTracking()
      .Where(x =>
        x.Id == stopId
        && db.LoadExecutionLegs.Any(link =>
          link.DispatchId == dispatchId
          && link.ExecutionLegId == x.ExecutionLegId
          && (
            x.DispatchId == dispatchId
            || link.StartVisitId == x.Id
            || link.EndVisitId == x.Id
          )
        )
      )
      .Select(x => new { x.Job, x.Name })
      .SingleOrDefaultAsync(ct);
    return visit is null ? null : $"{visit.Job} · {visit.Name}";
  }

  private Task<DispatchActivityEntry?> ReceiptAsync(
    AddDispatchActivityCommand request,
    CancellationToken ct
  ) =>
    db
      .DispatchActivityEntries.AsNoTracking()
      .SingleOrDefaultAsync(
        x =>
          x.DispatchId == request.DispatchId
          && x.AddOperationId == request.Update.OperationId,
        ct
      );

  private static RequestResponse<DispatchActivityItem> Replay(
    DispatchActivityEntry entry,
    AddDispatchActivityUpdate update,
    Guid actorId
  ) =>
    entry.ActorId == actorId
    && entry.Kind == update.Kind
    && entry.Text == update.Text.Trim()
    && entry.StopId == update.StopId
    && entry.DriverId == update.DriverId
    && entry.NeedsAttention == update.NeedsAttention
      ? RequestResponse<DispatchActivityItem>.Ok(
        DispatchActivityAccess.Item(entry)
      )
      : Conflict();

  private static RequestResponse<DispatchActivityItem> Conflict() =>
    RequestResponse<DispatchActivityItem>.Fail(
      "The activity changed. Refresh activity before saving your note.",
      409
    );
}
