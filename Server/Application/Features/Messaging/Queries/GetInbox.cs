using Application.Features.Messaging.Interfaces;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Messaging.Queries;

public sealed record GetInboxQuery(bool UnreadOnly)
  : IRequest<RequestResponse<InboxView>>;

// For the notice every page shows: how many conversations hold driver
// messages this dispatcher has not read (at most NoticeLimit, then More),
// and the highest company arrival sequence among them. A notice is due
// when that rises: a read, a claim or a reply never raises it, and a
// conversation that only comes into view because another was read carries
// an older sequence.
public sealed record GetUnreadNoticeQuery
  : IRequest<RequestResponse<UnreadNotice>>;

public sealed record UnreadNotice(int Conversations, bool More, long Newest);

// Revision: the conversation's revision in the view the dispatcher read.
public sealed record MarkConversationReadCommand(Guid Id, long Revision)
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
  IConversationReadMarkers markers,
  TimeProvider clock
)
  : IRequestHandler<GetInboxQuery, RequestResponse<InboxView>>,
    IRequestHandler<GetUnreadNoticeQuery, RequestResponse<UnreadNotice>>,
    IRequestHandler<MarkConversationReadCommand, RequestResponse<bool>>
{
  public const int PageSize = 50;
  public const int NoticeLimit = 99;

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
        || x.LastInboundRevision > 0
          && !db.ConversationReads.Any(r =>
            r.ConversationId == x.Id
            && r.UserId == user
            && r.ReadRevision >= x.LastInboundRevision
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

  // Two reads: the caller, then the arrival sequences of at most
  // NoticeLimit + 1 unread conversations, highest first. The database still
  // walks the company's conversations that have a driver message, in
  // sequence order (CompanyId, LastInboundSequence), and looks up this
  // dispatcher's marker for each (CompanyId, ConversationId, UserId): the
  // work grows with the company's conversations, not with their messages,
  // and has not been measured on PostgreSQL.
  public async Task<RequestResponse<UnreadNotice>> Handle(
    GetUnreadNoticeQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<UnreadNotice>.Fail("Access denied.", 403);
    var unread = await db
      .Conversations.AsNoTracking()
      .Where(c =>
        c.LastInboundRevision > 0
        && !db.ConversationReads.Any(r =>
          r.ConversationId == c.Id
          && r.UserId == user
          && r.ReadRevision >= c.LastInboundRevision
        )
      )
      .OrderByDescending(c => c.LastInboundSequence)
      .Select(c => c.LastInboundSequence)
      .Take(NoticeLimit + 1)
      .ToListAsync(ct);
    return RequestResponse<UnreadNotice>.Ok(
      new(
        Math.Min(unread.Count, NoticeLimit),
        unread.Count > NoticeLimit,
        unread.Count == 0 ? 0 : unread[0]
      )
    );
  }

  public async Task<RequestResponse<bool>> Handle(
    MarkConversationReadCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<bool>.Fail("Access denied.", 403);
    // A marker only moves forward, and never past the conversation's
    // current revision. The store keeps the higher of two writers' values
    // in one statement; a failure is not a conflict to hide, so it goes to
    // the request boundary.
    var current = await db
      .Conversations.Where(x => x.Id == request.Id)
      .Select(x => new { x.CompanyId, x.Revision })
      .SingleOrDefaultAsync(ct);
    if (current is null)
      return RequestResponse<bool>.Fail("Conversation not found.", 404);
    var through = Math.Min(request.Revision, current.Revision);
    if (through > 0)
      await markers.AdvanceAsync(
        current.CompanyId,
        request.Id,
        user,
        through,
        ct
      );
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
    var unread = await db
      .ConversationMessages.AsNoTracking()
      .Where(m =>
        ids.Contains(m.ConversationId)
        && m.Direction == MessageDirections.Inbound
        && !db.ConversationReads.Any(r =>
          r.ConversationId == m.ConversationId
          && r.UserId == user
          && r.ReadRevision >= m.ArrivedRevision
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
