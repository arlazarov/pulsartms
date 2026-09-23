using Application.Models;
using Domain.Entities.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Routing.Queries;

public sealed record GetInboxQuery(bool UnreadOnly)
  : IRequest<RequestResponse<InboxView>>;

public sealed record MarkConversationReadCommand(Guid Id, DateTime Through)
  : IRequest<RequestResponse<bool>>;

public sealed record ConversationSummary(
  Guid Id,
  string Participant,
  Guid? DriverId,
  string? DriverName,
  string LastPreview,
  DateTime LastMessageAt,
  DateTime? LastInboundAt,
  bool WindowOpen,
  int Unread,
  string? ClaimedBy,
  DateTime? ClaimedUntil,
  long Revision
);

public sealed record InboxView(
  IReadOnlyList<ConversationSummary> Conversations,
  bool More
);

// The company's conversations, newest first, with this dispatcher's unread
// counts. A fixed number of reads whatever the number of conversations:
// the page, the dispatcher's read markers, the unread counts grouped by
// conversation and the names shown. Unread is PulsR's own state per
// dispatcher; it tells the driver nothing.
public sealed class InboxHandlers(
  IAppDbContext db,
  ICurrentUser caller,
  TimeProvider clock
)
  : IRequestHandler<GetInboxQuery, RequestResponse<InboxView>>,
    IRequestHandler<MarkConversationReadCommand, RequestResponse<bool>>
{
  public const int PageSize = 50;

  public async Task<RequestResponse<InboxView>> Handle(
    GetInboxQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<InboxView>.Fail("Access denied.", 403);
    var now = clock.GetUtcNow().UtcDateTime;
    var page = await db
      .Conversations.AsNoTracking()
      .Where(x =>
        !request.UnreadOnly
        || db.ConversationMessages.Any(m =>
          m.ConversationId == x.Id
          && m.Direction == MessageDirections.Inbound
          && !db.ConversationReads.Any(r =>
            r.ConversationId == x.Id
            && r.UserId == user
            && r.ReadThrough >= m.SentAt
          )
        )
      )
      .OrderByDescending(x => x.LastMessageAt)
      .ThenBy(x => x.Id)
      .Take(PageSize + 1)
      .ToListAsync(ct);
    var shown = page.Take(PageSize).ToList();
    var summaries = await Inbox.SummariesAsync(db, user, shown, now, ct);
    return RequestResponse<InboxView>.Ok(new(summaries, page.Count > PageSize));
  }

  public async Task<RequestResponse<bool>> Handle(
    MarkConversationReadCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<bool>.Fail("Access denied.", 403);
    if (!await db.Conversations.AnyAsync(x => x.Id == request.Id, ct))
      return RequestResponse<bool>.Fail("Conversation not found.", 404);
    // A marker only moves forward, and never past what has arrived.
    var through = request.Through.ToUniversalTime();
    var newest = await db
      .ConversationMessages.Where(x => x.ConversationId == request.Id)
      .MaxAsync(x => (DateTime?)x.SentAt, ct);
    if (newest is { } last && through > last)
      through = last;
    var read = await db.ConversationReads.SingleOrDefaultAsync(
      x => x.ConversationId == request.Id && x.UserId == user,
      ct
    );
    if (read is null)
      db.ConversationReads.Add(
        new ConversationRead
        {
          Id = Guid.NewGuid(),
          ConversationId = request.Id,
          UserId = user,
          ReadThrough = through,
        }
      );
    else if (through > read.ReadThrough)
      read.ReadThrough = through;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      // Two tabs marking at once: the other one's marker stands.
      return RequestResponse<bool>.Ok(false);
    }
    return RequestResponse<bool>.Ok(true);
  }
}

// Reads shared by the inbox and a conversation.
public static class Inbox
{
  public static Task<Guid?> UserAsync(
    IAppDbContext db,
    ICurrentUser caller,
    CancellationToken ct
  ) =>
    db
      .Users.AsNoTracking()
      .Where(x => x.IdentityUserId == caller.IdentityUserId && x.IsActive)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);

  public static async Task<IReadOnlyList<ConversationSummary>> SummariesAsync(
    IAppDbContext db,
    Guid user,
    IReadOnlyList<Conversation> conversations,
    DateTime now,
    CancellationToken ct
  )
  {
    if (conversations.Count == 0)
      return [];
    var ids = conversations.Select(x => x.Id).ToArray();
    var reads = await db
      .ConversationReads.AsNoTracking()
      .Where(x => x.UserId == user && ids.Contains(x.ConversationId))
      .ToDictionaryAsync(x => x.ConversationId, x => x.ReadThrough, ct);
    var unread = await db
      .ConversationMessages.AsNoTracking()
      .Where(m =>
        ids.Contains(m.ConversationId)
        && m.Direction == MessageDirections.Inbound
        && !db.ConversationReads.Any(r =>
          r.ConversationId == m.ConversationId
          && r.UserId == user
          && r.ReadThrough >= m.SentAt
        )
      )
      .GroupBy(m => m.ConversationId)
      .Select(g => new { g.Key, Count = g.Count() })
      .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    var people = conversations
      .Select(x => x.DriverId)
      .OfType<Guid>()
      .Concat(conversations.Select(x => x.ClaimedBy).OfType<Guid>())
      .Distinct()
      .ToArray();
    var drivers = await db
      .Drivers.AsNoTracking()
      .Where(x => people.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    var users = await db
      .Users.AsNoTracking()
      .Where(x => people.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    return
    [
      .. conversations.Select(x => new ConversationSummary(
        x.Id,
        x.Participant,
        x.DriverId,
        x.DriverId is { } driver ? drivers.GetValueOrDefault(driver) : null,
        x.LastPreview,
        x.LastMessageAt,
        x.LastInboundAt,
        DriverMessageProgress.WindowOpen(x.LastInboundAt, now),
        unread.GetValueOrDefault(x.Id),
        x.ClaimedBy is { } by && x.ClaimedUntil > now
          ? users.GetValueOrDefault(by)
          : null,
        x.ClaimedUntil > now ? x.ClaimedUntil : null,
        x.Revision
      )),
    ];
  }
}
