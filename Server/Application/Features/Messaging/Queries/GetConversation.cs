using System.Runtime.CompilerServices;
using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;

namespace Application.Features.Messaging.Queries;

// A page of one conversation, newest first; Before continues backwards
// below the message it names. Seen is the conversation revision at which
// the pages the reader already shows were first read, when it asks for an
// older one.
// Around, instead of the newest page, is a window of the thread around one
// message - a search result - with up to Context messages on each side;
// it lets nothing be marked read, since the newest messages are not shown.
public sealed record GetConversationQuery(
  Guid Id,
  MessageCursor? Before,
  long? Seen = null,
  Guid? Around = null
) : IRequest<RequestResponse<ConversationView>>;

// A message's place in the thread's order: its time, when PulsR recorded
// it, and its id, so messages sharing a time are neither skipped nor
// repeated.
public sealed record MessageCursor(DateTime SentAt, DateTime CreatedAt, Guid Id)
{
  // The earlier API's before=time: every message older than it.
  public static MessageCursor Older(DateTime sentAt) =>
    new(sentAt, DateTime.MinValue, Guid.Empty);
}

// "Conversation X changed" signals for the caller's company, as they are
// raised after commit. A heartbeat (an empty id) keeps the stream open
// through proxies while nothing changes.
public sealed record StreamMessagingEventsQuery
  : IStreamRequest<MessagingEvent>;

public sealed record AttachmentView(
  Guid Id,
  string Name,
  string Type,
  string State,
  bool Available,
  string? FailureReason,
  IReadOnlyList<FiledView> Filed
);

// A load the file was filed to, as one of its documents.
public sealed record FiledView(Guid DispatchId, int LoadNumber, string Kind);

public sealed record MessageView(
  Guid Id,
  string Direction,
  string Kind,
  string Body,
  string Status,
  DateTime SentAt,
  string? Author,
  int? ErrorCode,
  IReadOnlyList<AttachmentView> Attachments
)
{
  // A later attempt of this reply exists (it was sent again): this one is
  // history and is not offered to be sent again.
  public bool Retried { get; init; }
}

// Next continues backwards. ReadThrough is the revision the reader may
// mark read after showing this page: it stops below the first unread
// driver message the reader has not been shown, such as one that arrived
// late with a time among older pages.
public sealed record ConversationView(
  ConversationSummary Summary,
  IReadOnlyList<MessageView> Messages,
  bool Older
)
{
  public MessageCursor? Next { get; init; }
  public long ReadThrough { get; init; }

  // A window around a message has newer messages above it.
  public bool Newer { get; init; }
}

public sealed class ConversationHandlers(
  IAppDbContext db,
  ICurrentUser caller,
  ICurrentCompany company,
  MessagingEvents events,
  TimeProvider clock
)
  : IRequestHandler<GetConversationQuery, RequestResponse<ConversationView>>,
    IStreamRequestHandler<StreamMessagingEventsQuery, MessagingEvent>
{
  public const int PageSize = 50;
  public const int Context = 25;
  public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(25);

  public async Task<RequestResponse<ConversationView>> Handle(
    GetConversationQuery request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<ConversationView>.Fail("Access denied.", 403);
    var conversation = await db
      .Conversations.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.Id, ct);
    if (conversation is null)
      return RequestResponse<ConversationView>.Fail(
        "Conversation not found.",
        404
      );
    var thread = db
      .ConversationMessages.AsNoTracking()
      .Where(x => x.ConversationId == request.Id);
    List<Row> rows;
    bool older,
      newer = false;
    if (request.Around is { } around)
    {
      var target = await Rows(thread.Where(x => x.Id == around))
        .SingleOrDefaultAsync(ct);
      if (target is null)
        return RequestResponse<ConversationView>.Fail(
          "Message not found.",
          404
        );
      var at = new MessageCursor(
        target.Message.SentAt,
        target.Message.CreatedAt,
        target.Message.Id
      );
      var above = await Rows(
          Above(thread, at)
            .OrderBy(x => x.SentAt)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Take(Context + 1)
        )
        .ToListAsync(ct);
      var below = await Rows(
          Below(thread, at)
            .OrderByDescending(x => x.SentAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(Context + 1)
        )
        .ToListAsync(ct);
      newer = above.Count > Context;
      older = below.Count > Context;
      rows = [.. above.Take(Context).Reverse(), target, .. below.Take(Context)];
    }
    else
    {
      var page = await Rows(
          Below(thread, request.Before)
            .OrderByDescending(x => x.SentAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(PageSize + 1)
        )
        .ToListAsync(ct);
      older = page.Count > PageSize;
      rows = page.Take(PageSize).ToList();
    }
    var messages = rows.Select(x => x.Message).ToList();
    var retried = rows.Where(x => x.Retried)
      .Select(x => x.Message.Id)
      .ToHashSet();
    var ids = messages.Select(x => x.Id).ToArray();
    var last = messages.LastOrDefault();
    // Opening a thread reads what was recorded before the driver messages
    // it shows, as a messenger does; what it must never mark is an unread
    // driver message the reader has not been shown that was recorded after
    // one it has. Below this page, that is one with a later arrival than
    // the lowest shown (delivered late with an older time); a page showing
    // no driver message lets nothing below it be marked. Among the pages
    // the reader already shows, it is one recorded after they were first
    // read (Seen), which the cap at Seen leaves unread. One aggregate over
    // the conversation's driver messages above the reader's marker.
    var seen = Math.Min(
      request.Seen ?? conversation.Revision,
      conversation.Revision
    );
    var low = messages
      .Where(x => x.Direction == MessageDirections.Inbound)
      .Min(x => (long?)x.ArrivedRevision);
    var noneShown = low is null;
    var lowest = low ?? 0;
    var bounded = last is not null;
    var (sentAt, createdAt, lastId) = last is null
      ? (DateTime.MinValue, DateTime.MinValue, Guid.Empty)
      : (last.SentAt, last.CreatedAt, last.Id);
    var blocking = await thread
      .Where(x =>
        x.Direction == MessageDirections.Inbound
        && x.ArrivedRevision
          > (
            db.ConversationReads.Where(r =>
                r.ConversationId == request.Id && r.UserId == user
              )
              .Select(r => (long?)r.ReadRevision)
              .FirstOrDefault() ?? 0
          )
        && !ids.Contains(x.Id)
        && (
          !bounded
          || (noneShown || x.ArrivedRevision > lowest)
            && (
              x.SentAt < sentAt
              || x.SentAt == sentAt
                && (
                  x.CreatedAt < createdAt
                  || x.CreatedAt == createdAt && x.Id.CompareTo(lastId) < 0
                )
            )
        )
      )
      .MinAsync(x => (long?)x.ArrivedRevision, ct);
    var attachments = (
      await db
        .MessageAttachments.AsNoTracking()
        .Where(x => ids.Contains(x.MessageId))
        .Select(x => new
        {
          x.Id,
          x.MessageId,
          x.OriginalName,
          x.DeclaredType,
          x.State,
          x.FailureReason,
          FileState = db
            .StoredFiles.Where(f => f.Id == x.StoredFileId)
            .Select(f => f.State)
            .FirstOrDefault(),
          FileName = db
            .StoredFiles.Where(f => f.Id == x.StoredFileId)
            .Select(f => f.Name)
            .FirstOrDefault(),
          Filed = db
            .DispatchDocuments.Where(d => d.SourceAttachmentId == x.Id)
            .Join(
              db.Dispatches,
              d => d.DispatchId,
              l => l.Id,
              (d, l) => new FiledView(l.Id, l.LoadNumber, d.Kind)
            )
            .ToList(),
        })
        .ToListAsync(ct)
    ).ToLookup(x => x.MessageId);
    var authors = messages
      .Select(x => x.AuthorId)
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    var names = await db
      .Users.AsNoTracking()
      .Where(x => authors.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    var summary = (
      await Inbox.SummariesAsync(
        db,
        user,
        [conversation],
        clock.GetUtcNow().UtcDateTime,
        ct
      )
    )[0];
    return RequestResponse<ConversationView>.Ok(
      new(
        summary,
        [
          .. messages.Select(x => new MessageView(
            x.Id,
            x.Direction,
            x.Kind,
            x.Body,
            x.Status,
            x.SentAt,
            x.AuthorId is { } author ? names.GetValueOrDefault(author) : null,
            x.ErrorCode,
            [
              .. attachments[x.Id]
                .Select(a => new AttachmentView(
                  a.Id,
                  a.FileName
                    ?? (a.OriginalName.Length > 0 ? a.OriginalName : "File"),
                  a.DeclaredType,
                  a.State,
                  a.FileState == StoredFileStates.Available,
                  a.FailureReason
                    ?? a.FileState switch
                    {
                      StoredFileStates.Rejected =>
                        "This kind of file is not accepted.",
                      StoredFileStates.Changed =>
                        "The file changed in its storage after it was "
                          + "checked, so it is not shown.",
                      _ => null,
                    },
                  a.Filed
                )),
            ]
          )
          {
            Retried = retried.Contains(x.Id),
          }),
        ],
        older
      )
      {
        Next = older ? new(last!.SentAt, last.CreatedAt, last.Id) : null,
        ReadThrough =
          request.Around is not null ? 0
          : blocking is { } first ? Math.Min(seen, first - 1)
          : seen,
        Newer = newer,
      }
    );
  }

  private sealed record Row(ConversationMessage Message, bool Retried);

  // Each message with whether a later attempt of it exists, in the same
  // statement: the retry key's index answers it per shown reply.
  private IQueryable<Row> Rows(IQueryable<ConversationMessage> page) =>
    page.Select(x => new Row(
      x,
      x.IdempotencyKey != null
        && db.ConversationMessages.Any(r =>
          r.Channel == x.Channel
          && r.BusinessNumberId == x.BusinessNumberId
          && r.IdempotencyKey == x.IdempotencyKey
          && r.Attempt > x.Attempt
        )
    ));

  // The messages before the cursor in the thread's order: newer ones.
  private static IQueryable<ConversationMessage> Above(
    IQueryable<ConversationMessage> messages,
    MessageCursor at
  ) =>
    messages.Where(x =>
      x.SentAt > at.SentAt
      || x.SentAt == at.SentAt
        && (
          x.CreatedAt > at.CreatedAt
          || x.CreatedAt == at.CreatedAt && x.Id.CompareTo(at.Id) > 0
        )
    );

  // The messages after the cursor in the thread's order (newest first).
  private static IQueryable<ConversationMessage> Below(
    IQueryable<ConversationMessage> messages,
    MessageCursor? cursor
  ) =>
    cursor is not { } at
      ? messages
      : messages.Where(x =>
        x.SentAt < at.SentAt
        || x.SentAt == at.SentAt
          && (
            x.CreatedAt < at.CreatedAt
            || x.CreatedAt == at.CreatedAt && x.Id.CompareTo(at.Id) < 0
          )
      );

  public async IAsyncEnumerable<MessagingEvent> Handle(
    StreamMessagingEventsQuery request,
    [EnumeratorCancellation] CancellationToken ct
  )
  {
    if (company.Id is not { } serving)
      yield break;
    using var subscription = events.Subscribe(serving);
    while (!ct.IsCancellationRequested)
    {
      // Queued signals were refused while this reader was behind: it says
      // so before the ones that did queue, which may come later than the
      // lost ones.
      if (subscription.TakeOverflow())
      {
        yield return MessagingEvent.Resync;
        continue;
      }
      using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
      wait.CancelAfter(Heartbeat);
      MessagingEvent? next = null;
      try
      {
        if (await subscription.Reader.WaitToReadAsync(wait.Token))
          subscription.Reader.TryRead(out next);
        else
          yield break;
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
      catch (OperationCanceledException)
      {
        yield break;
      }
      yield return next ?? MessagingEvent.KeepAlive;
    }
  }
}
