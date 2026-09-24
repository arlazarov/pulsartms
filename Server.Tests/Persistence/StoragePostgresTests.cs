using System.Data.Common;
using System.Security.Cryptography;
using Application.Diagnostics.Consistency;
using Application.Storage;
using Domain.Entities;
using Domain.Entities.Storage;
using Domain.Rules.Storage;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Server.Tests.Storage;

namespace Server.Tests.Persistence;

// Disconnecting a storage and recording an upload to it, interleaved on
// PostgreSQL: whichever commits second is refused, so no file is left
// pointing at a disconnected storage and no connection is disconnected
// under a file.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class StoragePostgresTests
{
  private static readonly byte[] Content = [1, 2, 3, 4];

  [RequiresPostgresFact]
  public async Task AnUploadRecordedAfterTheCheckStopsTheDisconnect()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var drive = await DriveAsync(fixture.Connect());
    var fileId = Guid.NewGuid();
    var upload = new UploadAfterCheck(fixture, drive, fileId);
    await using var db = fixture.Connect(upload);

    var result = await Handlers(db)
      .Handle(new DisconnectStorageCommand(drive, 1), default);

    Assert.True(upload.Recorded);
    Assert.Equal(409, result.StatusCode);
    await using var check = fixture.Connect();
    Assert.Equal(
      StorageConnectionStates.Connected,
      (await check.StorageConnections.SingleAsync(x => x.Id == drive)).State
    );
    Assert.Equal(
      drive,
      (await check.StoredFiles.SingleAsync(x => x.Id == fileId)).ConnectionId
    );
  }

  [RequiresPostgresFact]
  public async Task ADisconnectCommittedAfterTheUploadReadTheConnectionStopsTheUpload()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var drive = await DriveAsync(fixture.Connect());
    var disconnect = new DisconnectAfterRead(fixture, drive);
    await using var db = fixture.Connect(disconnect);

    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => PutAsync(db, drive, Guid.NewGuid())
    );

    Assert.True(disconnect.Committed);
    await using var check = fixture.Connect();
    Assert.Equal(
      StorageConnectionStates.Disconnected,
      (await check.StorageConnections.SingleAsync(x => x.Id == drive)).State
    );
    Assert.Empty(await check.StoredFiles.ToListAsync());
  }

  // Rows left from before the fence: the auditor reports a kept file in a
  // disconnected storage, and neither a refused upload there nor a file in
  // a connected storage.
  [RequiresPostgresFact]
  public async Task TheAuditorFindsAFileLeftOnADisconnectedStorage()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect();
    var drive = await DriveAsync(db);
    var kept = await PutAsync(fixture.Connect(), drive, Guid.NewGuid());
    var refused = await PutAsync(fixture.Connect(), drive, Guid.NewGuid());
    await db
      .StoredFiles.Where(x => x.Id == refused.Id)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(f => f.State, StoredFileStates.Rejected)
      );
    await db
      .StorageConnections.Where(x => x.Id == drive)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(c => c.State, StorageConnectionStates.Disconnected)
      );

    var page = await new StoredFileConnectionRule(db).ReadAsync(
      new(
        Company.Amf,
        DateTime.UtcNow,
        After: null,
        Limit: 10,
        PendingGrace: TimeSpan.FromMinutes(30)
      ),
      default
    );

    Assert.Equal(kept.Id.ToString(), Assert.Single(page.Observed).EntityKey);
  }

  private static async Task<Guid> DriveAsync(AppDbContext db)
  {
    await Targets(db).DefaultAsync(default);
    var drive = new StorageConnection
    {
      Id = Guid.NewGuid(),
      CompanyId = Company.Amf,
      Kind = Drive.Name,
      DisplayName = "Company drive",
      State = StorageConnectionStates.Connected,
      Revision = 1,
      CreatedAt = DateTime.UtcNow,
    };
    db.StorageConnections.Add(drive);
    await db.SaveChangesAsync();
    return drive.Id;
  }

  private static FileStore Store(AppDbContext db) =>
    new(
      db,
      Targets(db),
      new TestCompany(),
      Options.Create(new StorageOptions()),
      new StorageUploadGate(Options.Create(new StorageOptions())),
      TimeProvider.System
    );

  private static StorageTargets Targets(AppDbContext db) =>
    new(
      db,
      [
        new DatabaseFileStorage(db, FileStorageTests.Configuration()),
        new Drive(),
      ],
      FileStorageTests.Secrets,
      new TestCompany(),
      Options.Create(new StorageOptions()),
      TimeProvider.System
    );

  private static StorageConnectionHandlers Handlers(AppDbContext db) =>
    new(db, Targets(db), [], [], [], TimeProvider.System);

  private static Task<StoredFile> PutAsync(
    AppDbContext db,
    Guid drive,
    Guid fileId
  ) =>
    Store(db)
      .PutAsync(
        new(
          fileId,
          "pod.pdf",
          "application/pdf",
          Content.Length,
          Convert.ToHexStringLower(SHA256.HashData(Content)),
          ConnectionId: drive
        ),
        new MemoryStream(Content),
        default
      );

  // After the disconnect has checked that no file is there, an upload to
  // that storage is recorded and completes on another connection.
  private sealed class UploadAfterCheck(
    PostgresFixture fixture,
    Guid drive,
    Guid fileId
  ) : DbCommandInterceptor
  {
    public bool Recorded { get; private set; }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
      DbCommand command,
      CommandExecutedEventData eventData,
      DbDataReader result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        Recorded
        || !command.CommandText.Contains("\"StoredFiles\"")
        || !command.CommandText.Contains("EXISTS")
      )
        return result;
      Recorded = true;
      await using var other = fixture.Connect();
      await PutAsync(other, drive, fileId);
      return result;
    }
  }

  // After the upload's transaction has read the storage as connected, the
  // storage is disconnected and committed on another connection.
  private sealed class DisconnectAfterRead(PostgresFixture fixture, Guid drive)
    : DbCommandInterceptor
  {
    public bool Committed { get; private set; }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
      DbCommand command,
      CommandExecutedEventData eventData,
      DbDataReader result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        Committed
        || !command.CommandText.Contains("\"StorageConnections\"")
        || !command.CommandText.Contains("EXISTS")
      )
        return result;
      Committed = true;
      await using var other = fixture.Connect();
      var done = await Handlers(other)
        .Handle(new DisconnectStorageCommand(drive, 1), cancellationToken);
      Assert.True(done.Success, string.Join(";", done.Errors ?? []));
      return result;
    }
  }

  // A company drive that keeps nothing: it reads each upload to its end,
  // so the content is checked, and answers that no object exists.
  private sealed class Drive : IFileStorageProvider
  {
    public const string Name = "test-drive";
    public string Kind => Name;
    public bool IsAvailable => true;
    public long MaximumSize => 1024 * 1024;

    public Task<string> ReserveKeyAsync(
      StorageTarget target,
      Guid fileId,
      CancellationToken ct
    ) => Task.FromResult(fileId.ToString("N"));

    public Task PutAsync(
      StorageTarget target,
      string key,
      StorageUpload upload,
      CancellationToken ct
    ) => upload.Content.CopyToAsync(Stream.Null, ct);

    public Task<bool> ExistsAsync(
      StorageTarget target,
      string key,
      CancellationToken ct
    ) => Task.FromResult(false);

    public Task<Stream?> OpenAsync(
      StorageTarget target,
      string key,
      CancellationToken ct
    ) => Task.FromResult<Stream?>(null);

    public Task DeleteAsync(
      StorageTarget target,
      string key,
      CancellationToken ct
    ) => Task.CompletedTask;

    public Task<bool> CheckAsync(StorageTarget target, CancellationToken ct) =>
      Task.FromResult(true);
  }
}
