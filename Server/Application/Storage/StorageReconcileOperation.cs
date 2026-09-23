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
        && (
          x.UploadLeaseUntil == null
            ? x.UpdatedAt <= due
            : x.UploadLeaseUntil <= now
        )
      )
      .OrderBy(x => x.UpdatedAt)
      .Take(options.Value.ReconcileBatchSize)
      .ToListAsync(ct);
    var settled = 0;
    foreach (var connectionFiles in stalled.GroupBy(x => x.ConnectionId))
    {
      var connection = await db
        .StorageConnections.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == connectionFiles.Key, ct);
      if (connection is not { State: StorageConnectionStates.Connected })
        continue;
      IFileStorageProvider provider;
      StorageTarget target;
      try
      {
        provider = files.Provider(connection.Kind);
        target = files.Target(connection);
      }
      catch (StorageUnavailableException)
      {
        continue;
      }
      foreach (var file in connectionFiles)
      {
        if (await files.ClaimAsync(file.Id, ct) is not { } token)
          continue;
        bool exists;
        try
        {
          exists = await provider.ExistsAsync(target, file.ObjectKey, ct);
        }
        catch (StorageUnavailableException)
        {
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
    }
    // Files left quarantined unchecked, oldest first, a bounded few.
    var waiting = await db
      .StoredFiles.AsNoTracking()
      .Where(x =>
        x.State == StoredFileStates.Quarantined
        && x.UpdatedAt <= now.AddMinutes(-1)
      )
      .OrderBy(x => x.UpdatedAt)
      .Select(x => x.Id)
      .Take(options.Value.ReconcileBatchSize)
      .ToListAsync(ct);
    var check = scope.ServiceProvider.GetRequiredService<StoredFileCheck>();
    foreach (var id in waiting)
      if (await check.CheckAsync(id, ct) != StoredFileStates.Quarantined)
        settled++;
    return settled;
  }
}
