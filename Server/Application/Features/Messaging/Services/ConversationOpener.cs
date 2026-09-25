using Application.Features.Messaging.Interfaces;
using Domain.Entities.Messaging;

namespace Application.Features.Messaging.Services;

// The one way a conversation is started without a driver writing first:
// for a driver's number on the business number the company sends from
// now. An existing one is returned as it is; a new one has no message, no
// reply window and nothing unread. Two callers opening the same one at
// once meet on the conversation's unique key: the losing insert reads the
// winner's row. Commits on its own and signals after the commit.
public sealed class ConversationOpener(
  IAppDbContext db,
  IDriverMessaging messaging,
  ICurrentCompany company,
  MessagingEvents events,
  TimeProvider clock
)
{
  public async Task<Guid> OpenAsync(
    string number,
    string participant,
    Guid driverId,
    CancellationToken ct
  )
  {
    if (await ExistingAsync(number, participant, ct) is { } existing)
      return existing;
    var conversation = new Conversation
    {
      Id = Guid.NewGuid(),
      Channel = messaging.Channel,
      BusinessNumberId = number,
      Participant = participant,
      DriverId = driverId,
      LastMessageAt = clock.GetUtcNow().UtcDateTime,
      Revision = 1,
    };
    db.Conversations.Add(conversation);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      db.Entry(conversation).State = EntityState.Detached;
      return await ExistingAsync(number, participant, ct)
        ?? throw new InvalidOperationException(
          "A conversation insert failed and no conversation holds its key."
        );
    }
    if (company.Id is { } serving)
      events.Publish(serving, new(conversation.Id, conversation.Revision));
    return conversation.Id;
  }

  public Task<Guid?> ExistingAsync(
    string number,
    string participant,
    CancellationToken ct
  ) =>
    db
      .Conversations.AsNoTracking()
      .Where(x =>
        x.Channel == messaging.Channel
        && x.BusinessNumberId == number
        && x.Participant == participant
      )
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
}
