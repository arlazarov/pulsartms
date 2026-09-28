using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Commands;

// Whether this revision may send to the messaging provider, and who
// released it.
public sealed record SendHoldState(
  string Revision,
  bool Required,
  bool Held,
  DateTime? ReleasedAt,
  string? ReleasedBy
);

public sealed record GetSendHoldQuery
  : IRequest<RequestResponse<SendHoldState>>;

// An administrator's release of this revision, after seeing the platform
// drain the revision before it (the cutover plan). Recorded once; asking
// again answers with the first record.
public sealed record ReleaseSendsCommand
  : IRequest<RequestResponse<SendHoldState>>;

public sealed class SendHoldHandlers(
  IAppDbContext db,
  SendHold hold,
  ICurrentUser user,
  TimeProvider clock
)
  : IRequestHandler<GetSendHoldQuery, RequestResponse<SendHoldState>>,
    IRequestHandler<ReleaseSendsCommand, RequestResponse<SendHoldState>>
{
  public async Task<RequestResponse<SendHoldState>> Handle(
    GetSendHoldQuery request,
    CancellationToken ct
  ) => RequestResponse<SendHoldState>.Ok(await StateAsync(ct));

  public async Task<RequestResponse<SendHoldState>> Handle(
    ReleaseSendsCommand request,
    CancellationToken ct
  )
  {
    if (!await db.SendReleases.AnyAsync(x => x.Revision == hold.Revision, ct))
    {
      db.SendReleases.Add(
        new SendRelease
        {
          Id = Guid.NewGuid(),
          Revision = hold.Revision,
          ReleasedAt = clock.GetUtcNow().UtcDateTime,
          ReleasedBy = user.IdentityUserId ?? "",
        }
      );
      try
      {
        await db.SaveChangesAsync(ct);
      }
      // Two administrators at once: the first record stands.
      catch (DbUpdateException)
      {
        db.ChangeTracker.Clear();
        if (
          !await db
            .SendReleases.AsNoTracking()
            .AnyAsync(x => x.Revision == hold.Revision, ct)
        )
          throw;
      }
    }
    return RequestResponse<SendHoldState>.Ok(await StateAsync(ct));
  }

  private async Task<SendHoldState> StateAsync(CancellationToken ct)
  {
    var release = await db
      .SendReleases.AsNoTracking()
      .Where(x => x.Revision == hold.Revision)
      .Select(x => new { x.ReleasedAt, x.ReleasedBy })
      .SingleOrDefaultAsync(ct);
    return new(
      hold.Revision,
      hold.Required,
      await hold.HeldAsync(ct),
      release?.ReleasedAt,
      release?.ReleasedBy
    );
  }
}
