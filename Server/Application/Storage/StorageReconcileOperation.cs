using Domain.Entities.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Storage;

public interface IStorageReconcileOperation : IBackgroundOperation;

// Settles uploads that stopped without an answer. An upload nobody holds
// (its lease ran out, or it was recorded and never taken) is claimed with
// the same fenced lease an attempt uses, and its reserved key is asked for:
// an object there completes it as quarantined, because a provider completes
// an object only from content that matched the fingerprint; no object fails
// it. A connection that cannot be asked keeps the claim until it expires.
// Nothing is deleted here.
public sealed class StorageReconcileOperation(
  IServiceScopeFactory scopes,
  IOptions<StorageOptions> options,
  TimeProvider clock,
  ILogger<StorageReconcileOperation> logger
) : IStorageReconcileOperation
{
  public async Task RunAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        await CompanyPasses.ForEachCompanyAsync(
          scope.ServiceProvider,
          ReconcileOnceAsync,
          ct
        );
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Stored file reconciliation failed");
      }
      try
      {
        await Task.Delay(
          TimeSpan.FromMinutes(options.Value.ReconcileIntervalMinutes),
          clock,
          ct
        );
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  // Longest wait before a file that could not be settled is looked at
  // again, however often it failed.
  public static readonly TimeSpan MaximumBackoff = TimeSpan.FromHours(6);

  // Each pass takes the files due now, least recently tried first. A file
  // that cannot be settled - its storage disconnected or unreachable, or
  // its check unable to read it - is put back after a bounded backoff, so a
  // batch of them never keeps newer files from being reached. One file's
  // failure is that file's: it is logged and the pass goes on.
  public async Task<int> ReconcileOnceAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    var files = scope.ServiceProvider.GetRequiredService<FileStore>();
    var now = clock.GetUtcNow().UtcDateTime;
    var due = now.AddMinutes(-options.Value.UploadLeaseMinutes);
    var stalled = await db
      .StoredFiles.AsNoTracking()
      .Where(x =>
        x.State == StoredFileStates.Uploading
        && (x.ReconcileAfter == null || x.ReconcileAfter <= now)
        && (
          x.UploadLeaseUntil == null
            ? x.UpdatedAt <= due
            : x.UploadLeaseUntil <= now
        )
      )
      .OrderBy(x => x.ReconcileAfter ?? x.UpdatedAt)
      .ThenBy(x => x.Id)
      .Take(options.Value.ReconcileBatchSize)
      .ToListAsync(ct);
    var settled = 0;
    foreach (var connectionFiles in stalled.GroupBy(x => x.ConnectionId))
    {
      var connection = await db
        .StorageConnections.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == connectionFiles.Key, ct);
      (IFileStorageProvider Provider, StorageTarget Target)? usable = null;
      if (connection is { State: StorageConnectionStates.Connected })
        try
        {
          usable = (files.Provider(connection.Kind), files.Target(connection));
        }
        catch (StorageUnavailableException) { }
      foreach (var file in connectionFiles)
      {
        if (usable is not { } storage)
        {
          await DeferAsync(db, file, now, ct);
          continue;
        }
        try
        {
          if (await files.ClaimAsync(file.Id, ct) is not { } token)
            continue;
          bool exists;
          try
          {
            exists = await storage.Provider.ExistsAsync(
              storage.Target,
              file.ObjectKey,
              ct
            );
          }
          catch (StorageUnavailableException)
          {
            await DeferAsync(db, file, now, ct);
            continue;
          }
          if (
            await files.FinishAsync(
              file.Id,
              token,
              exists ? StoredFileStates.Quarantined : StoredFileStates.Failed,
              ct
            )
          )
            settled++;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
          logger.LogWarning(
            ex,
            "Stalled upload {StoredFileId} could not be settled",
            file.Id
          );
          await DeferAsync(db, file, now, ct);
        }
      }
    }
    // Files left quarantined unchecked, least recently tried first, a
    // bounded few.
    var waiting = await db
      .StoredFiles.AsNoTracking()
      .Where(x =>
        x.State == StoredFileStates.Quarantined
        && x.UpdatedAt <= now.AddMinutes(-1)
        && (x.ReconcileAfter == null || x.ReconcileAfter <= now)
      )
      .OrderBy(x => x.ReconcileAfter ?? x.UpdatedAt)
      .ThenBy(x => x.Id)
      .Take(options.Value.ReconcileBatchSize)
      .ToListAsync(ct);
    var check = scope.ServiceProvider.GetRequiredService<StoredFileCheck>();
    foreach (var file in waiting)
    {
      string state;
      try
      {
        state = await check.CheckAsync(file.Id, ct);
      }
      catch (Exception ex) when (!ct.IsCancellationRequested)
      {
        logger.LogWarning(
          ex,
          "Quarantined file {StoredFileId} could not be checked",
          file.Id
        );
        state = StoredFileStates.Quarantined;
      }
      if (state == StoredFileStates.Quarantined)
        await DeferAsync(db, file, now, ct);
      else
        settled++;
    }
    return settled;
  }

  // Not settled this time: tried again after a wait that doubles with each
  // consecutive failure, from the pass interval up to MaximumBackoff. Only
  // while the file is still in the state it was read in.
  private async Task DeferAsync(
    IAppDbContext db,
    StoredFile file,
    DateTime now,
    CancellationToken ct
  )
  {
    var wait = TimeSpan.FromMinutes(
      Math.Min(
        MaximumBackoff.TotalMinutes,
        Math.Max(1, options.Value.ReconcileIntervalMinutes)
          * Math.Pow(2, Math.Min(file.ReconcileFailures, 16))
      )
    );
    await db
      .StoredFiles.Where(x => x.Id == file.Id && x.State == file.State)
      .ExecuteUpdateAsync(
        x =>
          x.SetProperty(f => f.ReconcileAfter, now + wait)
            .SetProperty(
              f => f.ReconcileFailures,
              f => f.ReconcileFailures + 1
            ),
        ct
      );
  }
}
