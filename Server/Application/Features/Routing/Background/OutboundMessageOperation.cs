using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Messaging;
using Domain.Entities.Messaging;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Routing.Background;

public interface IOutboundMessageOperation : IBackgroundOperation;

// Sends queued replies. A lease alone does not make one sender - a worker
// can pause past its lease and still reach the provider - so each taking of
// a message increments its fence, and every later step is a conditional
// update on that fence:
//
// - "sending" is committed under the fence before the provider is called;
//   a worker that cannot commit it stops without calling.
// - The answer is recorded under the fence. A worker that lost the fence
//   may still write the provider's message id, because that answer is a
//   fact, but only onto a message still "sending" or "unknown" without an
//   id, and never as a second send.
// - A "sending" whose lease ran out is "unknown": the provider may or may
//   not have it, and only a dispatcher's retry sends it again.
//
// The database and the provider cannot be committed together: this
// promises no silent duplicate from PulsR, not exactly-once delivery.
public sealed class OutboundMessageOperation(
  IServiceScopeFactory scopes,
  MessagingEvents events,
  OutboxSignal signal,
  TimeProvider clock,
  ILogger<OutboundMessageOperation> logger
) : IOutboundMessageOperation
{
  public const int BatchSize = 10;

  // Longer than the provider's request timeout (20 seconds).
  public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
  public static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

  public async Task RunAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        await CompanyPasses.ForEachCompanyAsync(
          scope.ServiceProvider,
          async token => await RunOnceAsync(token),
          ct
        );
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Outbound message pass failed");
      }
      try
      {
        await signal.WaitAsync(Poll, ct);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  public async Task<int> RunOnceAsync(CancellationToken ct)
  {
    await ReapAsync(ct);
    List<Guid> due;
    var now = clock.GetUtcNow().UtcDateTime;
    await using (var scope = scopes.CreateAsyncScope())
      due = await scope
        .ServiceProvider.GetRequiredService<IAppDbContext>()
        .ConversationMessages.AsNoTracking()
        .Where(x =>
          x.Status == OutboundStates.Queued
          && (x.LeaseUntil == null || x.LeaseUntil <= now)
        )
        .OrderBy(x => x.CreatedAt)
        .Select(x => x.Id)
        .Take(BatchSize)
        .ToListAsync(ct);
    var sent = 0;
    foreach (var id in due)
    {
      await using var scope = scopes.CreateAsyncScope();
      if (await SendAsync(scope.ServiceProvider, id, ct))
        sent++;
    }
    return sent;
  }

  // A worker that died holding a message leaves it "sending"; after the
  // lease it is not known whether the provider has it.
  private async Task ReapAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    var now = clock.GetUtcNow().UtcDateTime;
    var stale = await db
      .ConversationMessages.AsNoTracking()
      .Where(x =>
        x.Status == DriverMessageStatuses.Sending && x.LeaseUntil <= now
      )
      .Select(x => new
      {
        x.Id,
        x.Fence,
        x.ConversationId,
      })
      .Take(BatchSize)
      .ToListAsync(ct);
    foreach (var message in stale)
      await FinishAsync(
        scope.ServiceProvider,
        message.Id,
        message.ConversationId,
        message.Fence,
        DriverMessageStatuses.Sending,
        DriverMessageStatuses.Unknown,
        null,
        null,
        ct
      );
  }

  private async Task<bool> SendAsync(
    IServiceProvider services,
    Guid id,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<IAppDbContext>();
    var now = clock.GetUtcNow().UtcDateTime;
    var message = await db
      .ConversationMessages.AsNoTracking()
      .SingleAsync(x => x.Id == id, ct);
    // The fence this worker takes is the one it saw plus one, set only if
    // nobody moved it since: the worker knows its own fence without reading
    // it back, and never mistakes another worker's for its own.
    var fence = message.Fence + 1;
    if (
      await db
        .ConversationMessages.Where(x =>
          x.Id == id
          && x.Fence == message.Fence
          && x.Status == OutboundStates.Queued
          && (x.LeaseUntil == null || x.LeaseUntil <= now)
        )
        .ExecuteUpdateAsync(
          x =>
            x.SetProperty(m => m.Fence, fence)
              .SetProperty(m => m.LeaseUntil, now + Lease),
          ct
        ) == 0
    )
      return false;
    var messaging = services.GetRequiredService<IDriverMessaging>();
    var conversation = await db
      .Conversations.AsNoTracking()
      .Where(x => x.Id == message.ConversationId)
      .Select(x => new
      {
        x.Participant,
        x.LastInboundAt,
        x.BusinessNumberId,
      })
      .SingleAsync(ct);
    // The queue can outlive the driver's window or the carrier's number:
    // checked again now, and a reply that can no longer go is withdrawn
    // without calling the provider.
    if (
      !DriverMessageProgress.WindowOpen(conversation.LastInboundAt, now)
      || await messaging.BusinessNumberAsync(ct)
        != conversation.BusinessNumberId
    )
      return await FinishAsync(
        services,
        id,
        message.ConversationId,
        fence,
        OutboundStates.Queued,
        DriverMessageStatuses.Withdrawn,
        null,
        null,
        ct
      );
    // Committed before the call: a worker that cannot say it is sending,
    // does not send.
    if (
      await db
        .ConversationMessages.Where(x =>
          x.Id == id && x.Fence == fence && x.Status == OutboundStates.Queued
        )
        .ExecuteUpdateAsync(
          x =>
            x.SetProperty(m => m.Status, DriverMessageStatuses.Sending)
              .SetProperty(m => m.StatusAt, now),
          ct
        ) == 0
    )
      return false;
    var result = await messaging.SendTextAsync(
      conversation.Participant,
      message.Body,
      ct
    );
    var status = result.Outcome switch
    {
      DriverMessageOutcome.Accepted => DriverMessageStatuses.Accepted,
      DriverMessageOutcome.Unknown => DriverMessageStatuses.Unknown,
      _ => DriverMessageStatuses.Rejected,
    };
    if (
      await FinishAsync(
        services,
        id,
        message.ConversationId,
        fence,
        DriverMessageStatuses.Sending,
        status,
        result.ProviderMessageId,
        result.ErrorCode,
        ct
      )
    )
      return true;
    if (result.ProviderMessageId is { } late)
      await LateAnswerAsync(services, id, message.ConversationId, late, ct);
    return false;
  }

  // Another worker holds this attempt now, or it was marked unknown. The
  // provider's answer is still a fact about this attempt - this row, never
  // a later retry, which is a row of its own - and is recorded, with the
  // conversation's revision, only while the attempt does not know its id.
  private async Task LateAnswerAsync(
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
  private async Task<bool> FinishAsync(
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
