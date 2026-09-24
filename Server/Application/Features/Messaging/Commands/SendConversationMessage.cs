using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Messaging.Commands;

// A reply typed in a conversation. LastSeenMessageId is the newest message
// the dispatcher had on screen; the reply is refused as stale when the
// driver or a colleague wrote since, unless the dispatcher confirms.
public sealed record SendConversationMessageCommand(
  Guid ConversationId,
  string Body,
  Guid IdempotencyKey,
  Guid? LastSeenMessageId,
  bool Confirm
) : IRequest<RequestResponse<MessageView>>;

// Sends an unanswered, refused or failed reply again, as a new attempt of
// the same retry key. Only a dispatcher's explicit request does this.
public sealed record RetryConversationMessageCommand(Guid MessageId)
  : IRequest<RequestResponse<MessageView>>;

// "I am answering this": colleagues see it for two minutes. A courtesy,
// never a lock on sending.
public sealed record ClaimConversationCommand(Guid ConversationId)
  : IRequest<RequestResponse<bool>>;

// Replies are committed as queued before anything is sent; the outbox
// worker sends them (see ReplyQueue and OutboundMessageOperation).
public sealed class ConversationReplies(
  IAppDbContext db,
  ICurrentUser caller,
  ICurrentCompany company,
  MessagingEvents events,
  ReplyQueue queue,
  TimeProvider clock
)
  : IRequestHandler<
    SendConversationMessageCommand,
    RequestResponse<MessageView>
  >,
    IRequestHandler<
      RetryConversationMessageCommand,
      RequestResponse<MessageView>
    >,
    IRequestHandler<ClaimConversationCommand, RequestResponse<bool>>
{
  public const int MaximumText = 4096;

  public async Task<RequestResponse<MessageView>> Handle(
    SendConversationMessageCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return Fail("Access denied.", 403);
    var body = request.Body?.Trim() ?? "";
    if (
      body.Length is 0 or > MaximumText
      || request.IdempotencyKey == Guid.Empty
    )
      return Fail($"Write between 1 and {MaximumText} characters.", 400);
    var conversation = await db.Conversations.SingleOrDefaultAsync(
      x => x.Id == request.ConversationId,
      ct
    );
    if (conversation is null)
      return Fail("Conversation not found.", 404);
    var check = await queue.CheckAsync(
      conversation,
      request.IdempotencyKey,
      user,
      request.LastSeenMessageId,
      request.Confirm,
      needsWindow: true,
      earlier =>
        earlier.Kind == ConversationMessageKinds.Text && earlier.Body == body,
      ct
    );
    if (check.Refused is { } refused)
      return Fail(refused.Message, refused.Status);
    if (check.Earlier is { } earlier)
      return RequestResponse<MessageView>.Ok(View(earlier));
    var message = queue.Queue(
      conversation,
      ConversationMessageKinds.Text,
      body,
      body,
      user,
      request.IdempotencyKey,
      1
    );
    return await queue.CommitAsync(conversation, ct) is { } failed
      ? Fail(failed.Message, failed.Status)
      : RequestResponse<MessageView>.Ok(View(message));
  }

  public async Task<RequestResponse<MessageView>> Handle(
    RetryConversationMessageCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return Fail("Access denied.", 403);
    var failed = await db
      .ConversationMessages.AsNoTracking()
      .SingleOrDefaultAsync(
        x =>
          x.Id == request.MessageId
          && x.Direction == MessageDirections.Outbound,
        ct
      );
    if (failed?.IdempotencyKey is not { } key)
      return Fail("Message not found.", 404);
    var latest = await db
      .ConversationMessages.AsNoTracking()
      .Where(x =>
        x.Channel == failed.Channel
        && x.BusinessNumberId == failed.BusinessNumberId
        && x.IdempotencyKey == key
      )
      .OrderByDescending(x => x.Attempt)
      .FirstAsync(ct);
    var now = clock.GetUtcNow().UtcDateTime;
    if (
      !(
        latest.Status
          is DriverMessageStatuses.Unknown
            or DriverMessageStatuses.Rejected
            or DriverMessageStatuses.Failed
            or DriverMessageStatuses.Withdrawn
        || DriverMessageProgress.Uncertain(latest.Status, latest.StatusAt, now)
      )
    )
      return Fail("This message is not waiting to be sent again.", 409);
    var conversation = await db.Conversations.SingleAsync(
      x => x.Id == failed.ConversationId,
      ct
    );
    if (
      latest.Kind != ConversationMessageKinds.Template
      && !DriverMessageProgress.WindowOpen(conversation.LastInboundAt, now)
    )
      return Fail(
        ReplyQueue.ClosedWindow.Message,
        ReplyQueue.ClosedWindow.Status
      );
    var message = queue.Queue(
      conversation,
      latest.Kind,
      latest.Body,
      conversation.LastPreview,
      user,
      key,
      latest.Attempt + 1,
      latest.Template
    );
    // A file reply sends the same stored file again.
    foreach (
      var attachment in await db
        .MessageAttachments.AsNoTracking()
        .Where(x => x.MessageId == latest.Id)
        .ToListAsync(ct)
    )
      db.MessageAttachments.Add(
        new MessageAttachment
        {
          Id = Guid.NewGuid(),
          MessageId = message.Id,
          StoredFileId = attachment.StoredFileId,
          DeclaredType = attachment.DeclaredType,
          OriginalName = attachment.OriginalName,
          Caption = attachment.Caption,
          State = attachment.State,
          NextAttemptAt = now,
          CreatedAt = now,
        }
      );
    return await queue.CommitAsync(conversation, ct) is { } refused
      ? Fail(refused.Message, refused.Status)
      : RequestResponse<MessageView>.Ok(View(message));
  }

  public async Task<RequestResponse<bool>> Handle(
    ClaimConversationCommand request,
    CancellationToken ct
  )
  {
    if (await Inbox.UserAsync(db, caller, ct) is not { } user)
      return RequestResponse<bool>.Fail("Access denied.", 403);
    var now = clock.GetUtcNow().UtcDateTime;
    var claimed = await db
      .Conversations.Where(x =>
        x.Id == request.ConversationId
        && (x.ClaimedBy == null || x.ClaimedBy == user || x.ClaimedUntil <= now)
      )
      .ExecuteUpdateAsync(
        x =>
          x.SetProperty(c => c.ClaimedBy, user)
            .SetProperty(c => c.ClaimedUntil, now + ReplyQueue.ClaimFor)
            .SetProperty(c => c.Revision, c => c.Revision + 1),
        ct
      );
    if (claimed == 1 && company.Id is { } serving)
      events.Publish(
        serving,
        new(
          request.ConversationId,
          await db
            .Conversations.AsNoTracking()
            .Where(x => x.Id == request.ConversationId)
            .Select(x => x.Revision)
            .SingleAsync(ct)
        )
      );
    return RequestResponse<bool>.Ok(claimed == 1);
  }

  public static MessageView View(ConversationMessage x) =>
    new(
      x.Id,
      x.Direction,
      x.Kind,
      x.Body,
      x.Status,
      x.SentAt,
      null,
      x.ErrorCode,
      []
    );

  private static RequestResponse<MessageView> Fail(
    string message,
    int status
  ) => RequestResponse<MessageView>.Fail(message, status);
}
