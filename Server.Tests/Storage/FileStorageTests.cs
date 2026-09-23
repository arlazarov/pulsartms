using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;
using Application.Storage;
using Domain.Entities;
using Domain.Entities.Storage;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Storage;

// The storage owner: an upload is recorded before the provider is called and
// completed after; a lost answer is settled by finding the object by its
// file id, never by deleting it; unchecked files are never served; and no
// company is silently given a store the server has not set up.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class FileStorageTests
{
  [Fact]
  public async Task AFileIsRecordedStoredAndServedOnlyOnceChecked()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var store = Store(f.Db);
    var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 proof of delivery");

    var file = await PutAsync(store, bytes, "../../etc/pod.pdf");

    var connection = await f.Db.StorageConnections.AsNoTracking().SingleAsync();
    Assert.Equal(
      (StorageKinds.Managed, true, StorageConnectionStates.Connected),
      (connection.Kind, connection.IsDefault, connection.State)
    );
    // Whatever the caller wanted, an upload ends quarantined.
    Assert.Equal(
      (
        "etc pod.pdf",
        (long)bytes.Length,
        StoredFileStates.Quarantined,
        Sha(bytes)
      ),
      (file.Name, file.Size, file.State, file.Sha256)
    );
    Assert.Null(await store.OpenAsync(file.Id, quarantined: false, default));
    Assert.NotNull(await store.OpenAsync(file.Id, quarantined: true, default));
    Assert.True(
      await store.SetStateAsync(
        file.Id,
        StoredFileStates.Quarantined,
        StoredFileStates.Available,
        default
      )
    );
    var opened = await store.OpenAsync(file.Id, quarantined: false, default);
    await using var content = opened!.Value.Content;
    using var copy = new MemoryStream();
    await content.CopyToAsync(copy);
    Assert.Equal(bytes, copy.ToArray());
  }

  // The provider stored the object but its answer never came back, or the
  // process stopped before recording it. While that attempt holds the
  // upload a retry is refused, not doubled; after its lease a retry with the
  // same id and content finds the object under the reserved key and
  // completes without a second upload. Nothing is deleted.
  [Fact]
  public async Task ALostAnswerIsCompletedByTheRetryWithoutASecondObject()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var provider = new CountingProvider(f.Db) { LoseAnswer = true };
    var id = Guid.NewGuid();
    var bytes = Encoding.UTF8.GetBytes("photo");

    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => PutAsync(Store(f.Db, provider, clock: clock), bytes, "a.jpg", id)
    );
    Assert.Equal(StoredFileStates.Uploading, (await Row(f)).State);
    await Assert.ThrowsAsync<StorageBusyException>(
      () => PutAsync(Store(f.Db, provider, clock: clock), bytes, "a.jpg", id)
    );

    clock.Advance(TimeSpan.FromMinutes(31));
    provider.LoseAnswer = false;
    var file = await PutAsync(
      Store(f.Db, provider, clock: clock),
      bytes,
      "a.jpg",
      id
    );

    Assert.Equal((1, 0), (provider.Puts, provider.Deletes));
    Assert.Equal(StoredFileStates.Quarantined, file.State);
    Assert.Single(await f.Db.ManagedFileBlobs.ToListAsync());
    var again = await PutAsync(
      Store(f.Db, provider, clock: clock),
      bytes,
      "a.jpg",
      id
    );
    Assert.Equal((file.Id, 1), (again.Id, provider.Puts));
  }

  [Fact]
  public async Task TheSameFileIdNeverStandsForDifferentContent()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var provider = new CountingProvider(f.Db) { LoseAnswer = true };
    var id = Guid.NewGuid();
    await Assert.ThrowsAsync<StorageUnavailableException>(
      () =>
        PutAsync(Store(f.Db, provider, clock: clock), [1, 2, 3], "a.bin", id)
    );
    clock.Advance(TimeSpan.FromMinutes(31));

    await Assert.ThrowsAsync<StorageConflictException>(
      () =>
        PutAsync(Store(f.Db, provider, clock: clock), [9, 9, 9], "a.bin", id)
    );
    Assert.Equal(Sha([1, 2, 3]), (await Row(f)).Sha256);

    provider.LoseAnswer = false;
    var file = await PutAsync(
      Store(f.Db, provider, clock: clock),
      [1, 2, 3],
      "a.bin",
      id
    );
    await Assert.ThrowsAsync<StorageConflictException>(
      () => PutAsync(Store(f.Db, provider, clock: clock), [7, 7], "a.bin", id)
    );
    Assert.Equal(file.Sha256, (await Row(f)).Sha256);
  }

  // The bytes do not match the hash the caller promised. The check fails
  // before the last byte is handed over, so no object exists, and the
  // upload is refused for good.
  [Fact]
  public async Task ContentThatDoesNotMatchItsHashLeavesNoObject()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var store = Store(f.Db);

    await Assert.ThrowsAsync<StorageContentMismatchException>(
      () =>
        store.PutAsync(
          new(
            Guid.NewGuid(),
            "a.bin",
            "application/octet-stream",
            3,
            Sha([1, 2, 3])
          ),
          new MemoryStream([1, 2, 4]),
          default
        )
    );

    Assert.Equal(StoredFileStates.Rejected, (await Row(f)).State);
    Assert.Empty(await f.Db.ManagedFileBlobs.ToListAsync());
  }

  // An attempt whose lease ran out was taken over and completed by another;
  // its late completion changes nothing.
  [Fact]
  public async Task ALateAttemptCannotCompleteAnUploadTakenOver()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var provider = new CountingProvider(f.Db) { LoseAnswer = true };
    var id = Guid.NewGuid();
    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => PutAsync(Store(f.Db, provider, clock: clock), [1], "a.bin", id)
    );
    clock.Advance(TimeSpan.FromMinutes(31));
    var late = await Store(f.Db, provider, clock: clock)
      .ClaimAsync(id, default);
    clock.Advance(TimeSpan.FromMinutes(31));
    provider.LoseAnswer = false;
    await PutAsync(Store(f.Db, provider, clock: clock), [1], "a.bin", id);

    Assert.False(
      await Store(f.Db, provider, clock: clock)
        .FinishAsync(id, late!.Value, StoredFileStates.Failed, default)
    );
    Assert.Equal(StoredFileStates.Quarantined, (await Row(f)).State);
  }

  [Fact]
  public async Task TheReconcilerSettlesOnlyUploadsNobodyHoldsAndDeletesNothing()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var provider = new CountingProvider(f.Db) { LoseAnswer = true };
    var reached = Guid.NewGuid();
    await Assert.ThrowsAsync<StorageUnavailableException>(
      () =>
        PutAsync(Store(f.Db, provider, clock: clock), [1, 2], "a.bin", reached)
    );
    provider.LoseAnswer = false;
    provider.RefusePut = true;
    var lost = Guid.NewGuid();
    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => PutAsync(Store(f.Db, provider, clock: clock), [3, 4], "b.bin", lost)
    );

    // Both are still held by their attempts: nothing to settle yet.
    Assert.Equal(
      0,
      await Reconciler(f, provider, clock).ReconcileOnceAsync(default)
    );

    clock.Advance(TimeSpan.FromMinutes(31));
    var held = Guid.NewGuid();
    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => PutAsync(Store(f.Db, provider, clock: clock), [5, 6], "c.bin", held)
    );
    var settled = await Reconciler(f, provider, clock)
      .ReconcileOnceAsync(default);

    Assert.Equal(2, settled);
    var states = await f
      .Db.StoredFiles.AsNoTracking()
      .ToDictionaryAsync(x => x.Id, x => x.State);
    Assert.Equal(StoredFileStates.Quarantined, states[reached]);
    Assert.Equal(StoredFileStates.Failed, states[lost]);
    Assert.Equal(StoredFileStates.Uploading, states[held]);
    Assert.Equal(0, provider.Deletes);
    Assert.Single(await f.Db.ManagedFileBlobs.ToListAsync());
  }

  // A file stored and then left unchecked (storage unreadable, a crash) is
  // checked by the reconciler's next pass.
  [Fact]
  public async Task TheReconcilerChecksFilesLeftQuarantined()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var provider = new CountingProvider(f.Db);
    var file = await PutAsync(
      Store(f.Db, provider, clock: clock),
      Encoding.UTF8.GetBytes("%PDF-1.7"),
      "a.pdf"
    );
    await f
      .Db.StoredFiles.Where(x => x.Id == file.Id)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(s => s.ContentType, "application/pdf")
      );

    Assert.Equal(
      0,
      await Reconciler(f, provider, clock).ReconcileOnceAsync(default)
    );
    clock.Advance(TimeSpan.FromMinutes(2));
    Assert.Equal(
      1,
      await Reconciler(f, provider, clock).ReconcileOnceAsync(default)
    );
    Assert.Equal(StoredFileStates.Available, (await Row(f)).State);
  }

  [Fact]
  public async Task AFileTooLargeEmptyOrUnhashedIsRefusedBeforeAnythingIsRecorded()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var store = Store(f.Db);

    await Assert.ThrowsAsync<ArgumentException>(() => PutAsync(store, [], "a"));
    await Assert.ThrowsAsync<ArgumentException>(
      () =>
        store.PutAsync(
          new(Guid.NewGuid(), "a", "text/plain", 17 * 1024 * 1024, Sha([1])),
          new MemoryStream(),
          default
        )
    );
    await Assert.ThrowsAsync<ArgumentException>(
      () =>
        store.PutAsync(
          new(Guid.NewGuid(), "a", "text/plain", 1, "not-a-hash"),
          new MemoryStream([1]),
          default
        )
    );
    Assert.Empty(await f.Db.StoredFiles.ToListAsync());
    Assert.Empty(await f.Db.ManagedFileBlobs.ToListAsync());
  }

  [Fact]
  public async Task AServerWithoutAManagedStoreNeverSubstitutesOne()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var store = Store(f.Db, managed: false);

    await Assert.ThrowsAsync<StorageUnavailableException>(
      () => PutAsync(store, [1], "a.bin")
    );
    Assert.Empty(await f.Db.StorageConnections.ToListAsync());
    Assert.Empty(await f.Db.StoredFiles.ToListAsync());
  }

  [Fact]
  public async Task AnotherCompanysFileIsNeitherListedNorOpened()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var file = await PutAsync(Store(f.Db), [1, 2, 3], "a.bin");
    await Store(f.Db)
      .SetStateAsync(file.Id, file.State, StoredFileStates.Available, default);
    var other = Guid.NewGuid();
    await f
      .Db.StoredFiles.IgnoreQueryFilters()
      .ExecuteUpdateAsync(x => x.SetProperty(s => s.CompanyId, other));
    f.Db.ChangeTracker.Clear();

    Assert.Null(await Store(f.Db).OpenAsync(file.Id, false, default));
  }

  [Fact]
  public async Task TheDefaultAlwaysHasAPlaceForNewFiles()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var managed = await Store(f.Db).DefaultAsync(default);
    var drive = new StorageConnection
    {
      Id = Guid.NewGuid(),
      CompanyId = Company.Amf,
      Kind = StorageKinds.GoogleDrive,
      DisplayName = "Drive",
      State = StorageConnectionStates.Connected,
      Revision = 1,
    };
    f.Db.StorageConnections.Add(drive);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();

    Assert.Equal(
      409,
      (
        await Handlers(f.Db)
          .Handle(new DisconnectStorageCommand(managed.Id, 1), default)
      ).StatusCode
    );
    Assert.Equal(
      409,
      (
        await Handlers(f.Db)
          .Handle(new SetDefaultStorageCommand(drive.Id, 7), default)
      ).StatusCode
    );
    Assert.True(
      (
        await Handlers(f.Db)
          .Handle(new SetDefaultStorageCommand(drive.Id, 1), default)
      ).Success
    );
    f.Db.ChangeTracker.Clear();
    Assert.Equal(
      [drive.Id],
      await f
        .Db.StorageConnections.Where(x => x.IsDefault)
        .Select(x => x.Id)
        .ToListAsync()
    );
    Assert.Equal(
      409,
      (
        await Handlers(f.Db)
          .Handle(new DisconnectStorageCommand(drive.Id, 2), default)
      ).StatusCode
    );
  }

  [Fact]
  public async Task EachKindSaysWhyItCannotBeUsedHere()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();

    var kinds = (
      await Handlers(f.Db, configured: false, managed: false)
        .Handle(new GetStorageSettingsQuery(), default)
    ).Response!.Kinds.ToDictionary(x => x.Kind);

    Assert.Equal(
      "PulsR storage is not set up on this server.",
      kinds[StorageKinds.Managed].Unavailable
    );
    Assert.Contains(
      "client registration",
      kinds[StorageKinds.GoogleDrive].Unavailable
    );
    Assert.Equal("Not available yet.", kinds[StorageKinds.Dropbox].Unavailable);
  }

  internal static Task<StoredFile> PutAsync(
    FileStore store,
    byte[] bytes,
    string name,
    Guid? id = null
  ) =>
    store.PutAsync(
      new(
        id ?? Guid.NewGuid(),
        name,
        "application/octet-stream",
        bytes.Length,
        Sha(bytes)
      ),
      new MemoryStream(bytes),
      default
    );

  internal static string Sha(byte[] bytes) =>
    Convert.ToHexStringLower(SHA256.HashData(bytes));

  private static Task<StoredFile> Row(DispatchSyncFixture f) =>
    f.Db.StoredFiles.AsNoTracking().SingleAsync();

  internal static FileStore Store(
    AppDbContext db,
    IFileStorageProvider? provider = null,
    bool managed = true,
    TimeProvider? clock = null
  ) =>
    new(
      db,
      [provider ?? new DatabaseFileStorage(db, Configuration(managed))],
      Secrets,
      new TestCompany(),
      Options.Create(new StorageOptions()),
      new StorageUploadGate(Options.Create(new StorageOptions())),
      clock ?? TimeProvider.System
    );

  internal static IConfiguration Configuration(bool managed = true) =>
    new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["Storage:Managed:Provider"] = managed ? "database" : null,
        }
      )
      .Build();

  internal static readonly IStorageSecrets Secrets = new StorageSecrets(
    new EphemeralDataProtectionProvider()
  );

  private static StorageReconcileOperation Reconciler(
    DispatchSyncFixture f,
    IFileStorageProvider provider,
    TimeProvider clock
  )
  {
    var services = new ServiceCollection();
    services.AddScoped<IAppDbContext>(_ => f.NewContext());
    services.AddScoped(sp =>
      Store(
        (AppDbContext)sp.GetRequiredService<IAppDbContext>(),
        provider,
        clock: clock
      )
    );
    services.AddScoped<StoredFileCheck>();
    return new(
      services
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>(),
      Options.Create(new StorageOptions()),
      clock,
      NullLogger<StorageReconcileOperation>.Instance
    );
  }

  private static StorageConnectionHandlers Handlers(
    AppDbContext db,
    bool configured = true,
    bool managed = true
  ) =>
    new(
      db,
      Store(db, managed: managed),
      [new DatabaseFileStorage(db, Configuration(managed))],
      [new FakeAuthorization { IsConfigured = configured }],
      [new FakePicker { IsConfigured = configured }],
      TimeProvider.System
    );

  // The database store, with a provider whose answer can be lost after it
  // stored the object, or which refuses before storing anything.
  private sealed class CountingProvider(AppDbContext db) : IFileStorageProvider
  {
    private readonly DatabaseFileStorage inner = new(db, Configuration());
    public bool LoseAnswer { get; set; }
    public bool RefusePut { get; set; }
    public int Puts { get; private set; }
    public int Deletes { get; private set; }
    public string Kind => inner.Kind;
    public bool IsAvailable => true;
    public long MaximumSize => inner.MaximumSize;

    public Task<string> ReserveKeyAsync(
      StorageTarget target,
      Guid fileId,
      CancellationToken ct
    ) => inner.ReserveKeyAsync(target, fileId, ct);

    public async Task PutAsync(
      StorageTarget target,
      string key,
      StorageUpload upload,
      CancellationToken ct
    )
    {
      if (RefusePut)
        throw new StorageUnavailableException("Refused.");
      Puts++;
      await inner.PutAsync(target, key, upload, ct);
      if (LoseAnswer)
        throw new StorageUnavailableException("No answer.");
    }

    public Task<bool> ExistsAsync(
      StorageTarget target,
      string key,
      CancellationToken ct
    ) => inner.ExistsAsync(target, key, ct);

    public Task<Stream?> OpenAsync(
      StorageTarget target,
      string key,
      CancellationToken ct
    ) => inner.OpenAsync(target, key, ct);

    public Task DeleteAsync(
      StorageTarget target,
      string key,
      CancellationToken ct
    )
    {
      Deletes++;
      return inner.DeleteAsync(target, key, ct);
    }

    public Task<bool> CheckAsync(StorageTarget target, CancellationToken ct) =>
      Task.FromResult(true);
  }
}

internal sealed class FakeAuthorization : IStorageAuthorization
{
  public string Kind => StorageKinds.GoogleDrive;
  public bool IsConfigured { get; set; } = true;
  public bool Crash { get; set; }
  public int Exchanges { get; private set; }
  public string? Verifier { get; private set; }

  public Uri AuthorizationUrl(string state, string codeChallenge) =>
    new($"https://consent.example.invalid/?state={state}&c={codeChallenge}");

  public Task<StorageGrant?> ExchangeAsync(
    string code,
    string codeVerifier,
    CancellationToken ct
  )
  {
    Exchanges++;
    Verifier = codeVerifier;
    if (Crash)
      throw new HttpRequestException("The process lost the provider.");
    return Task.FromResult<StorageGrant?>(
      code == "good" ? new("{\"RefreshToken\":\"refresh-secret\"}") : null
    );
  }
}

internal sealed class FakePicker : IStorageRootPicker
{
  public string Kind => StorageKinds.GoogleDrive;
  public bool IsConfigured { get; set; } = true;

  public Task<StoragePickerSession?> SessionAsync(
    StorageTarget target,
    CancellationToken ct
  ) =>
    Task.FromResult<StoragePickerSession?>(
      target.Secret?.Contains("refresh-secret") == true
        ? new("access", "client", "key", "123")
        : null
    );

  public Task<StorageRoot?> VerifyAsync(
    StorageTarget target,
    string folderId,
    CancellationToken ct
  ) =>
    Task.FromResult(
      folderId == "shared-folder-id-1"
        ? new StorageRoot(folderId, "Loads", "shared-drive-1")
        : null
    );
}
