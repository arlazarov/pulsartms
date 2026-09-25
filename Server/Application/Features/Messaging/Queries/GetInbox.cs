using Application.Features.Messaging.Interfaces;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Messaging.Queries;

// After continues the list below the conversation it names, in the same
// order; Search narrows it to a driver's name or a number's digits.
// Archived lists only the conversations of drivers who are no longer
// active (Driver.IsActive, the fleet's own lifecycle). The plain list
// leaves them out; Unread and a search keep them, each marked by its
// driver's status, so nothing unread or searched for is hidden. Nothing
// about the conversation or its history changes with the driver's status.
public sealed record GetInboxQuery(
  bool UnreadOnly,
  string? Search = null,
  InboxCursor? After = null,
  bool InChosenGroup = false,
  bool Archived = false
) : IRequest<RequestResponse<InboxView>>;

// A position in the list: a conversation's last message time and id.
public sealed record InboxCursor(DateTime At, Guid Id);

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
)
{
  // The linked driver's status, not the messages': true while active,
  // false once no longer active, null for a number linked to no driver.
  public bool? DriverActive { get; init; }
}

// Next continues the list, when there is more of it.
public sealed record InboxView(
  IReadOnlyList<ConversationSummary> Conversations,
  bool More
)
{
  public InboxCursor? Next { get; init; }
}

// The company's conversations, newest first, with this dispatcher's unread
// counts. A fixed number of reads whatever the number of conversations:
// the page, the dispatcher's read markers, the unread counts grouped by
// conversation and the names shown. Unread is PulsR's own state per
// dispatcher; it tells the driver nothing.
public sealed class InboxHandlers(
  IAppDbContext db,
  ICurrentUser caller,
  IConversationReadMarkers markers,
  IDriverScope scope,
  TimeProvider clock
)
  : IRequestHandler<GetInboxQuery, RequestResponse<InboxView>>,
    IRequestHandler<GetUnreadNoticeQuery, RequestResponse<UnreadNotice>>,
    IRequestHandler<MarkConversationReadCommand, RequestResponse<bool>>
{
  public const int PageSize = 50;
  public const int NoticeLimit = 99;
  public const int MaximumSearch = 100;

  public async Task<RequestResponse<InboxView>> Handle(
    GetInboxQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<InboxView>.Fail("Access denied.", 403);
    var term = request.Search?.Trim() ?? "";
    if (term.Length > MaximumSearch)
      return RequestResponse<InboxView>.Fail(
        $"Search for at most {MaximumSearch} characters.",
        400
      );
    var now = clock.GetUtcNow().UtcDateTime;
    var query = db
      .Conversations.AsNoTracking()
      .Where(x =>
        !request.UnreadOnly
        || x.LastInboundRevision > 0
          && !db.ConversationReads.Any(r =>
            r.ConversationId == x.Id
            && r.UserId == user
            && r.ReadRevision >= x.LastInboundRevision
          )
      );
    // The dispatcher's chosen driver group: conversations linked to one of
    // its drivers. Unlinked ones are under All. The unread notice is not
    // narrowed: what is unread stays each dispatcher's own, whatever group
    // they are looking at.
    if (
      request.InChosenGroup
      && await scope.CurrentAsync(ct) is { IsAll: false } group
    )
    {
      var drivers = group.Drivers;
      query = query.Where(x =>
        x.DriverId != null && drivers.Contains(x.DriverId.Value)
      );
    }
    if (request.Archived)
      query = query.Where(x =>
        db.Drivers.Any(d => d.Id == x.DriverId && !d.IsActive)
      );
    else if (!request.UnreadOnly && term.Length == 0)
      query = query.Where(x =>
        x.DriverId == null
        || !db.Drivers.Any(d => d.Id == x.DriverId && !d.IsActive)
      );
    if (term.Length > 0)
    {
      var name = term.ToLowerInvariant();
      var digits = new string([.. term.Where(char.IsAsciiDigit)]);
      query = query.Where(x =>
        digits.Length >= 3 && x.Participant.Contains(digits)
        || db.Drivers.Any(d =>
          d.Id == x.DriverId && d.Name.ToLower().Contains(name)
        )
      );
    }
    // A keyset on the list's own order: the rows after the cursor, whatever
    // arrived above it meanwhile. A conversation that moves up while the
    // dispatcher pages is not shown twice; it is at the top on the next
    // read of the first page.
    if (request.After is { } after)
      query = query.Where(x =>
        x.LastMessageAt < after.At
        || x.LastMessageAt == after.At && x.Id.CompareTo(after.Id) > 0
      );
    var page = await query
      .OrderByDescending(x => x.LastMessageAt)
      .ThenBy(x => x.Id)
      .Take(PageSize + 1)
      .ToListAsync(ct);
    var shown = page.Take(PageSize).ToList();
    var summaries = await Inbox.SummariesAsync(db, user, shown, now, ct);
    return RequestResponse<InboxView>.Ok(
      new(summaries, page.Count > PageSize)
      {
        Next =
          page.Count > PageSize
            ? new(shown[^1].LastMessageAt, shown[^1].Id)
            : null,
      }
    );
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
      .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.IsActive }, ct);
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
        x.DriverId is { } driver
          ? drivers.GetValueOrDefault(driver)?.Name
          : null,
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
      )
      {
        DriverActive = x.DriverId is { } linked
          ? drivers.GetValueOrDefault(linked)?.IsActive
          : null,
      }),
    ];
  }
}
