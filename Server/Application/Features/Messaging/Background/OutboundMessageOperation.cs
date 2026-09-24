using System.Text.Json;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Services;
using Application.Storage;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;
using Domain.Models.Messaging;
using Domain.Rules.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Messaging.Background;

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
  private readonly OutboxRecords records = new(events, clock);

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
      await records.FinishAsync(
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
    // without calling the provider. A template needs no window.
    var template = message.Kind == ConversationMessageKinds.Template;
    // A template goes only while it is approved for the number the
    // conversation is on; one withdrawn since it was queued is not sent.
    var approved =
      !template
      || Template(message) is { } queued
        && await services
          .GetRequiredService<ApprovedTemplates>()
          .FindAsync(
            conversation.BusinessNumberId,
            queued.Name,
            queued.Language,
            ct
          )
          is { } current
        && current.Parameters == queued.Parameters.Count;
    Stream? content = null;
    DriverFile? file = null;
    if (message.Kind == ConversationMessageKinds.File)
      (content, file) = await FileAsync(services, message, ct);
    await using var owned = content;
    if (
      !template
        && !DriverMessageProgress.WindowOpen(conversation.LastInboundAt, now)
      || await messaging.BusinessNumberAsync(ct)
        != conversation.BusinessNumberId
      || message.Kind == ConversationMessageKinds.File && file is null
      || !approved
    )
      return await records.FinishAsync(
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
    var result = message.Kind switch
    {
      ConversationMessageKinds.File => await messaging.SendFileAsync(
        conversation.BusinessNumberId,
        conversation.Participant,
        file!,
        ct
      ),
      ConversationMessageKinds.Template => await messaging.SendTemplateAsync(
        conversation.BusinessNumberId,
        conversation.Participant,
        Template(message)!.Name,
        Template(message)!.Language,
        Template(message)!.Parameters,
        ct
      ),
      _ => await messaging.SendTextAsync(
        conversation.BusinessNumberId,
        conversation.Participant,
        message.Body,
        ct
      ),
    };
    var status = result.Outcome switch
    {
      DriverMessageOutcome.Accepted => DriverMessageStatuses.Accepted,
      DriverMessageOutcome.Unknown => DriverMessageStatuses.Unknown,
      // The number moved after the check above; nothing was sent.
      DriverMessageOutcome.NumberChanged => DriverMessageStatuses.Withdrawn,
      _ => DriverMessageStatuses.Rejected,
    };
    if (
      await records.FinishAsync(
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
      await records.LateAnswerAsync(
        services,
        id,
        message.ConversationId,
        late,
        ct
      );
    return false;
  }

  // The reply's stored file, when it is still available to send.
  private static async Task<(Stream?, DriverFile?)> FileAsync(
    IServiceProvider services,
    ConversationMessage message,
    CancellationToken ct
  )
  {
    var attachment = await services
      .GetRequiredService<IAppDbContext>()
      .MessageAttachments.AsNoTracking()
      .Where(x => x.MessageId == message.Id && x.StoredFileId != null)
      .FirstOrDefaultAsync(ct);
    if (attachment?.StoredFileId is not { } id)
      return (null, null);
    (StoredFile File, Stream Content)? opened;
    try
    {
      opened = await services
        .GetRequiredService<FileStore>()
        .OpenAsync(id, quarantined: false, ct);
    }
    catch (StorageUnavailableException)
    {
      opened = null;
    }
    return opened is { } found
      ? (
        found.Content,
        new(
          found.Content,
          found.File.Size,
          found.File.ContentType,
          found.File.Name,
          attachment.Caption
        )
      )
      : (null, null);
  }

  private static TemplatePayload? Template(ConversationMessage message)
  {
    try
    {
      return message.Template is null
        ? null
        : JsonSerializer.Deserialize<TemplatePayload>(message.Template);
    }
    catch (JsonException)
    {
      return null;
    }
  }
}
