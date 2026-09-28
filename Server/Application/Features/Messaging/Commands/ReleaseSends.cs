using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Commands;

// Whether this revision may send to the messaging provider, and who
// released it.
public sealed record SendHoldState(
  string? Revision,
  bool Required,
  bool Held,
  DateTime? ReleasedAt,
  string? ReleasedBy
);

public sealed record GetSendHoldQuery
  : IRequest<RequestResponse<SendHoldState>>;

// A deployment operator's release of this revision, after seeing the
// platform drain the revision before it (the cutover plan). It reaches
// every carrier, so a carrier's Admin who is not an operator is refused
// here as well as by the endpoint's policy. Recorded once; asking again
// answers with the first record. A revision without a valid name is not
// released.
public sealed record ReleaseSendsCommand
  : IRequest<RequestResponse<SendHoldState>>;

public sealed class SendHoldHandlers(
  IAppDbContext db,
  SendHold hold,
  ICurrentUser user,
  IDeploymentOperators operators,
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
    if (!operators.Includes(user.IdentityUserId))
      return RequestResponse<SendHoldState>.Fail(
        "Only a deployment operator releases sends.",
        403
      );
    if (hold.Revision is not { } revision)
      return RequestResponse<SendHoldState>.Fail(
        "This process has no deployment revision name, so its sends stay "
          + "held.",
        409
      );
    if (!await db.SendReleases.AnyAsync(x => x.Revision == revision, ct))
    {
      db.SendReleases.Add(
        new SendRelease
        {
          Id = Guid.NewGuid(),
          Revision = revision,
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
            .AnyAsync(x => x.Revision == revision, ct)
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
