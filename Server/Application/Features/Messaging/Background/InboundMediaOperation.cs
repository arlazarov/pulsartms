using Application.Diagnostics;
using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Services;
using Application.Storage;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;
using Domain.Rules.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Messaging.Background;

public interface IInboundMediaOperation : IBackgroundOperation;

// Copies files drivers sent into the company's storage before the
// provider's media id expires. Each attachment is claimed with a fenced
// lease and copied in its own unit of work under the attachment's id, so a
// repeated copy is the same stored file. The provider's hash is the file's
// fingerprint: bytes that differ are refused, never stored. The file lands
// quarantined in the inbox for its day; nothing here decides what it is.
public sealed class InboundMediaOperation(
  IServiceScopeFactory scopes,
  MessagingEvents events,
  TimeProvider clock,
  ILogger<InboundMediaOperation> logger
) : IInboundMediaOperation
{
  public const int BatchSize = 5;
  public static readonly TimeSpan Lease = TimeSpan.FromMinutes(15);
  public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

  public async Task RunAsync(CancellationToken ct)
  {
    BackgroundProgress.Expect("InboundMedia", Interval);
    while (!ct.IsCancellationRequested)
    {
      BackgroundProgress.Started("InboundMedia");
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
        logger.LogWarning(ex, "Inbound media copy pass failed");
      }
      try
      {
        await Task.Delay(Interval, clock, ct);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  public async Task<int> RunOnceAsync(CancellationToken ct)
  {
    List<Guid> due;
    var now = clock.GetUtcNow().UtcDateTime;
    await using (var scope = scopes.CreateAsyncScope())
      due = await scope
        .ServiceProvider.GetRequiredService<IAppDbContext>()
        .MessageAttachments.AsNoTracking()
        .Where(x =>
          x.State == MessageAttachmentStates.Pending
          && x.NextAttemptAt <= now
          && (x.LeaseUntil == null || x.LeaseUntil <= now)
        )
        .OrderBy(x => x.NextAttemptAt)
        .Select(x => x.Id)
        .Take(BatchSize)
        .ToListAsync(ct);
    var settled = 0;
    foreach (var id in due)
    {
      await using var scope = scopes.CreateAsyncScope();
      if (await CopyAsync(scope.ServiceProvider, id, ct))
        settled++;
    }
    return settled;
  }

  private async Task<bool> CopyAsync(
    IServiceProvider services,
    Guid id,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<IAppDbContext>();
    var now = clock.GetUtcNow().UtcDateTime;
    var token = Guid.NewGuid();
    if (
      await db
        .MessageAttachments.Where(x =>
          x.Id == id
          && x.State == MessageAttachmentStates.Pending
          && (x.LeaseUntil == null || x.LeaseUntil <= now)
        )
        .ExecuteUpdateAsync(
          x =>
            x.SetProperty(a => a.LeaseToken, token)
              .SetProperty(a => a.LeaseUntil, now + Lease)
              .SetProperty(a => a.Attempts, a => a.Attempts + 1),
          ct
        ) == 0
    )
      return false;
    var attachment = await db
      .MessageAttachments.AsNoTracking()
      .SingleAsync(x => x.Id == id, ct);
    var message = await db
      .ConversationMessages.AsNoTracking()
      .SingleAsync(x => x.Id == attachment.MessageId, ct);
    Outcome outcome;
    try
    {
      outcome = await CopyAsync(services, attachment, message, now, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
      when (ex
          is DriverMessagingUnavailableException
            or StorageUnavailableException
            or StorageBusyException
      )
    {
      outcome = Outcome.Later(ex.GetType().Name);
    }
    catch (Exception ex)
      when (ex is StorageContentMismatchException or StorageConflictException)
    {
      outcome = Outcome.Fail(
        "The file did not match what the provider described."
      );
    }
    if (outcome.State == MessageAttachmentStates.Pending)
    {
      if (attachment.MediaExpiresAt <= now)
        outcome = Outcome.Fail("The file expired before it could be copied.");
      else
        logger.LogWarning(
          "Inbound file {AttachmentId} not copied yet ({Reason}); "
            + "attempt {Attempt}",
          id,
          outcome.Reason,
          attachment.Attempts
        );
    }
    var backoff = TimeSpan.FromSeconds(
      Math.Min(3600, 30 * Math.Pow(2, Math.Min(attachment.Attempts, 7)))
    );
    // The attachment's outcome and the conversation's revision commit
    // together, and only for the pass still holding the lease; the signal
    // goes out after the commit. A crash before the commit leaves the
    // attachment pending under its lease, to be taken up again after it.
    long? revision = null;
    await using (var transaction = await db.Database.BeginTransactionAsync(ct))
    {
      var finished =
        await db
          .MessageAttachments.Where(x => x.Id == id && x.LeaseToken == token)
          .ExecuteUpdateAsync(
            x =>
              x.SetProperty(a => a.State, outcome.State)
                .SetProperty(a => a.StoredFileId, outcome.File)
                .SetProperty(a => a.FailureReason, outcome.Failure)
                .SetProperty(a => a.NextAttemptAt, now + backoff)
                .SetProperty(a => a.LeaseToken, (Guid?)null)
                .SetProperty(a => a.LeaseUntil, (DateTime?)null),
            ct
          ) == 1;
      if (!finished)
        return false;
      if (outcome.State != MessageAttachmentStates.Pending)
      {
        await db
          .Conversations.Where(x => x.Id == message.ConversationId)
          .ExecuteUpdateAsync(
            x => x.SetProperty(c => c.Revision, c => c.Revision + 1),
            ct
          );
        revision = await db
          .Conversations.AsNoTracking()
          .Where(x => x.Id == message.ConversationId)
          .Select(x => x.Revision)
          .SingleAsync(ct);
      }
      await transaction.CommitAsync(ct);
    }
    if (
      revision is { } committed
      && services.GetService<ICurrentCompany>()?.Id is { } company
    )
      events.Publish(company, new(message.ConversationId, committed));
    return revision is not null;
  }

  private async Task<Outcome> CopyAsync(
    IServiceProvider services,
    MessageAttachment attachment,
    ConversationMessage message,
    DateTime now,
    CancellationToken ct
  )
  {
    if (attachment.MediaExpiresAt <= now || attachment.ProviderMediaId is null)
      return Outcome.Fail("The file expired before it could be copied.");
    var messaging = services.GetRequiredService<IDriverMessaging>();
    var files = services.GetRequiredService<FileStore>();
    var targets = services.GetRequiredService<StorageTargets>();
    var layouts = services.GetRequiredService<StorageLayouts>();
    var media = await messaging.OpenMediaAsync(attachment.ProviderMediaId, ct);
    if (media is null)
      return Outcome.Fail("The provider no longer has this file.");
    await using var content = media.Content;
    var connection = await targets.DefaultAsync(ct);
    if (media.Length > targets.MaximumSize(targets.Provider(connection.Kind)))
      return Outcome.Fail("The file is larger than PulsR stores.");
    var name = FileName(attachment, media.MimeType, message.SentAt);
    var file = await files.PutAsync(
      new(
        attachment.Id,
        name,
        media.MimeType,
        media.Length,
        media.Sha256,
        await layouts.InboxFolderAsync(
          DateOnly.FromDateTime(message.SentAt),
          ct
        ),
        attachment.OriginalName.Length > 0 ? attachment.OriginalName : name,
        connection.Id
      ),
      content,
      ct
    );
    // Checked at once; one left unchecked (storage unreadable, a crash) is
    // checked again by the storage reconciler.
    if (file.State == StoredFileStates.Quarantined)
      await services
        .GetRequiredService<StoredFileCheck>()
        .CheckAsync(file.Id, ct);
    // Only a durable, complete file is stored. An upload this pass did not
    // finish (another attempt took it over) is tried again later.
    return file.State switch
    {
      StoredFileStates.Quarantined or StoredFileStates.Available => new(
        MessageAttachmentStates.Stored,
        file.Id,
        null,
        null
      ),
      StoredFileStates.Uploading => Outcome.Later("upload not finished"),
      StoredFileStates.Missing => Outcome.Fail("The stored copy is missing."),
      _ => Outcome.Fail("The file did not match what the provider described."),
    };
  }

  // A document keeps its own name; anything else is named for what it is
  // and when it arrived, never for a guess about its content.
  public static string FileName(
    MessageAttachment attachment,
    string mimeType,
    DateTime sentAt
  )
  {
    if (attachment.OriginalName.Length > 0)
      return StorageNaming.Segment(attachment.OriginalName);
    var kind = mimeType.Split('/')[0] switch
    {
      "image" => "Photo",
      "audio" => "Voice note",
      "video" => "Video",
      _ => "File",
    };
    var extension = mimeType.Split(';')[0].Trim() switch
    {
      "image/jpeg" => ".jpg",
      "image/png" => ".png",
      "image/webp" => ".webp",
      "application/pdf" => ".pdf",
      "audio/ogg" => ".ogg",
      "audio/mpeg" => ".mp3",
      "audio/aac" => ".aac",
      "video/mp4" => ".mp4",
      _ => "",
    };
    return StorageNaming.Segment(
      $"{kind} {sentAt:yyyy.MM.dd HH.mm.ss}{extension}"
    );
  }

  private sealed record Outcome(
    string State,
    Guid? File,
    string? Failure,
    string? Reason
  )
  {
    public static Outcome Fail(string why) =>
      new(MessageAttachmentStates.Failed, null, why, null);

    public static Outcome Later(string reason) =>
      new(MessageAttachmentStates.Pending, null, null, reason);
  }
}
