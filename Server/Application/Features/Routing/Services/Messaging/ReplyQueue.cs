using Domain.Entities.Messaging;
using Domain.Rules.Messaging;

namespace Application.Features.Routing.Services.Messaging;

// The rules every reply is queued by, whatever it carries: the retry key
// returns the first reply, the driver's window must be open (templates
// excepted), and a reply to a conversation that moved on since the
// dispatcher looked is refused unless confirmed. Queued replies commit
// with the conversation's revision; the signal and the outbox wake follow
// the commit.
public sealed class ReplyQueue(
  IAppDbContext db,
  ICurrentCompany company,
  MessagingEvents events,
  OutboxSignal outbox,
  TimeProvider clock
)
{
  public static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(2);

  public sealed record Refusal(string Message, int Status);

  public sealed record Check(ConversationMessage? Earlier, Refusal? Refused);

  public async Task<Check> CheckAsync(
    Conversation conversation,
    Guid key,
    Guid user,
    Guid? lastSeen,
    bool confirm,
    bool needsWindow,
    Func<ConversationMessage, bool> same,
    CancellationToken ct
  )
  {
    var earlier = await db
      .ConversationMessages.AsNoTracking()
      .Where(x =>
        x.Channel == conversation.Channel
        && x.BusinessNumberId == conversation.BusinessNumberId
        && x.IdempotencyKey == key
      )
      .OrderByDescending(x => x.Attempt)
      .FirstOrDefaultAsync(ct);
    if (earlier is not null)
      return earlier.ConversationId == conversation.Id && same(earlier)
        ? new(earlier, null)
        : new(null, new("The retry key belongs to another message.", 409));
    if (
      needsWindow
      && !DriverMessageProgress.WindowOpen(
        conversation.LastInboundAt,
        clock.GetUtcNow().UtcDateTime
      )
    )
      return new(null, ClosedWindow);
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
      !confirm
      && newest is not null
      && newest.Id != lastSeen
      && (
        newest.Direction == MessageDirections.Inbound || newest.AuthorId != user
      )
    )
      return new(
        null,
        new(
          "A newer message arrived since you started. Read it, then send "
            + "again or confirm.",
          409
        )
      );
    return new(null, null);
  }

  public static readonly Refusal ClosedWindow = new(
    "The driver has not written in the last 24 hours, so WhatsApp accepts "
      + "only an approved template.",
    409
  );

  public ConversationMessage Queue(
    Conversation conversation,
    string kind,
    string body,
    string preview,
    Guid user,
    Guid key,
    int attempt,
    string? template = null
  )
  {
    var now = clock.GetUtcNow().UtcDateTime;
    var message = new ConversationMessage
    {
      Id = Guid.NewGuid(),
      ConversationId = conversation.Id,
      Channel = conversation.Channel,
      BusinessNumberId = conversation.BusinessNumberId,
      Direction = MessageDirections.Outbound,
      Kind = kind,
      Body = body,
      Template = template,
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
    conversation.LastPreview = preview.Length <= 200 ? preview : preview[..200];
    conversation.ClaimedBy = user;
    conversation.ClaimedUntil = now + ClaimFor;
    conversation.Revision++;
    return message;
  }

  // Null when committed.
  public async Task<Refusal?> CommitAsync(
    Conversation conversation,
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
      return new("The conversation changed. Try again.", 409);
    }
    if (company.Id is { } serving)
      events.Publish(serving, new(conversation.Id, conversation.Revision));
    outbox.Wake();
    return null;
  }
}
