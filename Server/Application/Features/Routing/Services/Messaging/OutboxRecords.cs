using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Routing.Services.Messaging;

// The outbox's state changes, each in one transaction with the
// conversation's revision and followed by the change signal: a reply
// settled under the fence that holds it, and a provider's late answer
// recorded on its own attempt.
public sealed class OutboxRecords(MessagingEvents events, TimeProvider clock)
{
  // Another worker holds this attempt now, or it was marked unknown. The
  // provider's answer is still a fact about this attempt - this row, never
  // a later retry, which is a row of its own - and is recorded, with the
  // conversation's revision, only while the attempt does not know its id.
  public async Task LateAnswerAsync(
    IServiceProvider services,
    Guid id,
    Guid conversation,
    string providerId,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<IAppDbContext>();
    long revision;
    await using (var transaction = await db.Database.BeginTransactionAsync(ct))
    {
      if (
        await db
          .ConversationMessages.Where(x =>
            x.Id == id
            && x.ProviderMessageId == null
            && (
              x.Status == DriverMessageStatuses.Sending
              || x.Status == DriverMessageStatuses.Unknown
            )
          )
          .ExecuteUpdateAsync(
            x =>
              x.SetProperty(m => m.ProviderMessageId, providerId)
                .SetProperty(m => m.Status, DriverMessageStatuses.Accepted)
                .SetProperty(m => m.StatusAt, clock.GetUtcNow().UtcDateTime),
            ct
          ) == 0
      )
        return;
      revision = await BumpAsync(db, conversation, ct);
      await transaction.CommitAsync(ct);
    }
    Publish(services, conversation, revision);
  }

  // The status and the conversation's revision commit together, only for
  // the fence that holds the message; the signal follows the commit.
  public async Task<bool> FinishAsync(
    IServiceProvider services,
    Guid id,
    Guid conversation,
    long fence,
    string from,
    string status,
    string? providerId,
    int? errorCode,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<IAppDbContext>();
    var now = clock.GetUtcNow().UtcDateTime;
    long revision;
    await using (var transaction = await db.Database.BeginTransactionAsync(ct))
    {
      if (
        await db
          .ConversationMessages.Where(x =>
            x.Id == id && x.Fence == fence && x.Status == from
          )
          .ExecuteUpdateAsync(
            x =>
              x.SetProperty(m => m.Status, status)
                .SetProperty(m => m.StatusAt, now)
                .SetProperty(m => m.ProviderMessageId, providerId)
                .SetProperty(m => m.ErrorCode, errorCode)
                .SetProperty(m => m.LeaseUntil, (DateTime?)null),
            ct
          ) == 0
      )
        return false;
      revision = await BumpAsync(db, conversation, ct);
      await transaction.CommitAsync(ct);
    }
    Publish(services, conversation, revision);
    return true;
  }

  private static async Task<long> BumpAsync(
    IAppDbContext db,
    Guid conversation,
    CancellationToken ct
  )
  {
    await db
      .Conversations.Where(x => x.Id == conversation)
      .ExecuteUpdateAsync(
        x => x.SetProperty(c => c.Revision, c => c.Revision + 1),
        ct
      );
    return await db
      .Conversations.AsNoTracking()
      .Where(x => x.Id == conversation)
      .Select(x => x.Revision)
      .SingleAsync(ct);
  }

  private void Publish(
    IServiceProvider services,
    Guid conversation,
    long revision
  )
  {
    if (services.GetService<ICurrentCompany>()?.Id is { } company)
      events.Publish(company, new(conversation, revision));
  }
}
