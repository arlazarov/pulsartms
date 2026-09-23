using System.Runtime.CompilerServices;
using Application.Features.Routing.Services.Messaging;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;

namespace Application.Features.Routing.Queries;

// A page of one conversation, newest first; Before continues backwards.
public sealed record GetConversationQuery(Guid Id, DateTime? Before)
  : IRequest<RequestResponse<ConversationView>>;

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
  string? FailureReason
);

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
);

public sealed record ConversationView(
  ConversationSummary Summary,
  IReadOnlyList<MessageView> Messages,
  bool Older
);

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
    var page = await db
      .ConversationMessages.AsNoTracking()
      .Where(x =>
        x.ConversationId == request.Id
        && (request.Before == null || x.SentAt < request.Before)
      )
      .OrderByDescending(x => x.SentAt)
      .ThenByDescending(x => x.CreatedAt)
      .Take(PageSize + 1)
      .ToListAsync(ct);
    var messages = page.Take(PageSize).ToList();
    var ids = messages.Select(x => x.Id).ToArray();
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
                    ?? (
                      a.FileState == StoredFileStates.Rejected
                        ? "This kind of file is not accepted."
                        : null
                    )
                )),
            ]
          )),
        ],
        page.Count > PageSize
      )
    );
  }

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
      yield return next ?? new(Guid.Empty, 0);
    }
  }
}
