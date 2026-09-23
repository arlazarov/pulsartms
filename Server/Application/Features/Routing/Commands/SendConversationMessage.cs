using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Messaging;
using Application.Models;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Routing.Commands;

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
// worker sends them. A repeated request with the same retry key returns the
// first reply instead of queueing another.
public sealed class ConversationReplies(
  IAppDbContext db,
  ICurrentUser caller,
  ICurrentCompany company,
  MessagingEvents events,
  OutboxSignal outbox,
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
  public static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(2);

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
    var earlier = await db
      .ConversationMessages.AsNoTracking()
      .Where(x =>
        x.Channel == conversation.Channel
        && x.BusinessNumberId == conversation.BusinessNumberId
        && x.IdempotencyKey == request.IdempotencyKey
      )
      .OrderByDescending(x => x.Attempt)
      .FirstOrDefaultAsync(ct);
    if (earlier is not null)
      return earlier.ConversationId == conversation.Id && earlier.Body == body
        ? RequestResponse<MessageView>.Ok(View(earlier))
        : Fail("The retry key belongs to another message.", 409);
    var now = clock.GetUtcNow().UtcDateTime;
    if (!DriverMessageProgress.WindowOpen(conversation.LastInboundAt, now))
      return Fail(
        "The driver has not written in the last 24 hours, so WhatsApp "
          + "accepts only an approved template.",
        409
      );
    var newest = await db
      .ConversationMessages.AsNoTracking()
      .Where(x => x.ConversationId == conversation.Id)
      .OrderByDescending(x => x.SentAt)
      .ThenByDescending(x => x.CreatedAt)
      .Select(x => new
      {
        x.Id,
        x.AuthorId,
        x.Direction,
      })
      .FirstOrDefaultAsync(ct);
    if (
      !request.Confirm
      && newest is not null
      && newest.Id != request.LastSeenMessageId
      && (
        newest.Direction == MessageDirections.Inbound || newest.AuthorId != user
      )
    )
      return Fail(
        "A newer message arrived since you started. Read it, then send "
          + "again or confirm.",
        409
      );
    var message = Queue(
      conversation,
      body,
      user,
      request.IdempotencyKey,
      1,
      now
    );
    conversation.ClaimedBy = user;
    conversation.ClaimedUntil = now + ClaimFor;
    return await CommitAsync(conversation, message, ct);
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
        || DriverMessageProgress.Uncertain(latest.Status, latest.StatusAt, now)
      )
    )
      return Fail("This message is not waiting to be sent again.", 409);
    var conversation = await db.Conversations.SingleAsync(
      x => x.Id == failed.ConversationId,
      ct
    );
    if (!DriverMessageProgress.WindowOpen(conversation.LastInboundAt, now))
      return Fail(
        "The driver has not written in the last 24 hours, so WhatsApp "
          + "accepts only an approved template.",
        409
      );
    var message = Queue(
      conversation,
      latest.Body,
      user,
      key,
      latest.Attempt + 1,
      now
    );
    return await CommitAsync(conversation, message, ct);
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
            .SetProperty(c => c.ClaimedUntil, now + ClaimFor)
            .SetProperty(c => c.Revision, c => c.Revision + 1),
        ct
      );
    if (claimed == 1)
      await PublishAsync(request.ConversationId, ct);
    return RequestResponse<bool>.Ok(claimed == 1);
  }

  private ConversationMessage Queue(
    Conversation conversation,
    string body,
    Guid user,
    Guid key,
    int attempt,
    DateTime now
  )
  {
    var message = new ConversationMessage
    {
      Id = Guid.NewGuid(),
      ConversationId = conversation.Id,
      Channel = conversation.Channel,
      BusinessNumberId = conversation.BusinessNumberId,
      Direction = MessageDirections.Outbound,
      Kind = ConversationMessageKinds.Text,
      Body = body,
      AuthorId = user,
      IdempotencyKey = key,
      Attempt = attempt,
      Status = OutboundStates.Queued,
      StatusAt = now,
      SentAt = now,
      CreatedAt = now,
    };
    db.ConversationMessages.Add(message);
    conversation.LastMessageAt = now;
    conversation.LastMessageId = message.Id;
    conversation.LastPreview = body.Length <= 200 ? body : body[..200];
    conversation.Revision++;
    return message;
  }

  private async Task<RequestResponse<MessageView>> CommitAsync(
    Conversation conversation,
    ConversationMessage message,
    CancellationToken ct
  )
  {
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception ex)
      when (db.IsWriteConflict(ex) || ex is DbUpdateException)
    {
      // A same-key request or another change got there first: reading
      // again shows which.
      return Fail("The conversation changed. Try again.", 409);
    }
    if (company.Id is { } serving)
      events.Publish(serving, new(conversation.Id, conversation.Revision));
    outbox.Wake();
    return RequestResponse<MessageView>.Ok(View(message));
  }

  private async Task PublishAsync(Guid conversation, CancellationToken ct)
  {
    var revision = await db
      .Conversations.AsNoTracking()
      .Where(x => x.Id == conversation)
      .Select(x => x.Revision)
      .SingleAsync(ct);
    if (company.Id is { } serving)
      events.Publish(serving, new(conversation, revision));
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
