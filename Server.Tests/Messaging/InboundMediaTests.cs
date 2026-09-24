using System.Data.Common;
using System.Text;
using Application.Features.Messaging.Background;
using Application.Features.Messaging.Interfaces;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Application.Storage;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;
using Domain.Models.Messaging;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Messaging;

// Files drivers send are copied into the company's storage before the
// provider's media id expires: once, under the attachment's own id, checked
// before release, in the inbox for their day; a file whose bytes differ
// from the provider's hash is refused; a provider that does not answer is
// asked again later, and an expired or vanished file fails visibly.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class InboundMediaTests
{
  private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 bol");

  [Fact]
  public async Task AFileIsCopiedOnceCheckedAndFiledInTheInboxForItsDay()
  {
    await using var f = await Fixture.CreateAsync();
    f.Messaging.Media["1234567890"] = (Pdf, "application/pdf", null);
    var id = await f.ReceiveAsync("bol 1407.pdf");
    using var listening = f.Events.Subscribe(Company.Amf);

    Assert.Equal(1, await f.Worker.RunOnceAsync(default));
    Assert.Equal(0, await f.Worker.RunOnceAsync(default));

    var attachment = await f.Attachment(id);
    Assert.Equal(
      (MessageAttachmentStates.Stored, id),
      (attachment.State, attachment.StoredFileId)
    );
    var file = await f.Db.StoredFiles.AsNoTracking().SingleAsync();
    Assert.Equal(
      (StoredFileStates.Available, "bol 1407.pdf", "Inbox/2026.09.21"),
      (file.State, file.Name, file.Folder)
    );
    Assert.Equal(1, f.Messaging.Downloads);
    Assert.True(listening.Reader.TryRead(out _));
  }

  [Fact]
  public async Task BytesThatDoNotMatchTheProvidersHashAreRefused()
  {
    await using var f = await Fixture.CreateAsync();
    f.Messaging.Media["1234567890"] = (
      Pdf,
      "application/pdf",
      new string('0', 64)
    );
    var id = await f.ReceiveAsync("");

    await f.Worker.RunOnceAsync(default);

    var attachment = await f.Attachment(id);
    Assert.Equal(MessageAttachmentStates.Failed, attachment.State);
    Assert.Contains("did not match", attachment.FailureReason);
    Assert.Empty(await f.Db.ManagedFileBlobs.ToListAsync());
  }

  [Fact]
  public async Task AProviderThatDoesNotAnswerIsAskedAgainLater()
  {
    await using var f = await Fixture.CreateAsync();
    f.Messaging.Unavailable = true;
    var id = await f.ReceiveAsync("");

    await f.Worker.RunOnceAsync(default);
    await f.Worker.RunOnceAsync(default);

    var attachment = await f.Attachment(id);
    Assert.Equal(
      (MessageAttachmentStates.Pending, 1, (Guid?)null),
      (attachment.State, attachment.Attempts, attachment.LeaseToken)
    );
    Assert.True(attachment.NextAttemptAt > f.Clock.GetUtcNow().UtcDateTime);
    Assert.Equal(1, f.Messaging.Downloads);

    f.Clock.Advance(TimeSpan.FromHours(1));
    f.Messaging.Unavailable = false;
    f.Messaging.Media["1234567890"] = (Pdf, "application/pdf", null);
    await f.Worker.RunOnceAsync(default);
    Assert.Equal(
      MessageAttachmentStates.Stored,
      (await f.Attachment(id)).State
    );
  }

  [Fact]
  public async Task AnExpiredOrVanishedFileFailsVisibly()
  {
    await using var f = await Fixture.CreateAsync();
    var vanished = await f.ReceiveAsync("");
    await f.Worker.RunOnceAsync(default);
    Assert.Equal(
      "The provider no longer has this file.",
      (await f.Attachment(vanished)).FailureReason
    );

    f.Messaging.Unavailable = true;
    var expired = await f.ReceiveAsync("", "wamid.2");
    f.Clock.Advance(InboxRecorder.MediaLifetime + TimeSpan.FromMinutes(1));
    await f.Worker.RunOnceAsync(default);
    var attachment = await f.Attachment(expired);
    Assert.Equal(MessageAttachmentStates.Failed, attachment.State);
    Assert.Contains("expired", attachment.FailureReason);
  }

  // Another pass holds the attachment: it is left alone until that lease
  // runs out, then taken over.
  [Fact]
  public async Task AnAttachmentHeldByAnotherPassIsLeftUntilItsLeaseEnds()
  {
    await using var f = await Fixture.CreateAsync();
    f.Messaging.Media["1234567890"] = (Pdf, "application/pdf", null);
    var id = await f.ReceiveAsync("");
    var until = f.Clock.GetUtcNow().UtcDateTime.AddMinutes(10);
    await f
      .Db.MessageAttachments.Where(x => x.Id == id)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(a => a.LeaseToken, Guid.NewGuid())
          .SetProperty(a => a.LeaseUntil, until)
      );

    Assert.Equal(0, await f.Worker.RunOnceAsync(default));
    Assert.Equal(0, f.Messaging.Downloads);

    f.Clock.Advance(TimeSpan.FromMinutes(11));
    Assert.Equal(1, await f.Worker.RunOnceAsync(default));
    Assert.Equal(
      MessageAttachmentStates.Stored,
      (await f.Attachment(id)).State
    );
  }

  // The process dies after the attachment's outcome is written and before
  // the conversation's revision is: neither commits, the attachment stays
  // pending under its lease, and after the lease it is settled once, with
  // one signal.
  [Fact]
  public async Task ACrashBetweenTheTwoUpdatesCommitsNeither()
  {
    await using var f = await Fixture.CreateAsync();
    f.Messaging.Media["1234567890"] = (Pdf, "application/pdf", null);
    var id = await f.ReceiveAsync("");
    var before = await f.RevisionAsync();
    var crash = new FailOnConversationUpdate();
    f.Interceptors.Add(crash);

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Worker.RunOnceAsync(default)
    );

    var attachment = await f.Attachment(id);
    Assert.Equal(MessageAttachmentStates.Pending, attachment.State);
    Assert.NotNull(attachment.LeaseToken);
    Assert.Equal(before, await f.RevisionAsync());

    f.Interceptors.Remove(crash);
    using var listening = f.Events.Subscribe(Company.Amf);
    Assert.Equal(0, await f.Worker.RunOnceAsync(default));
    f.Clock.Advance(InboundMediaOperation.Lease + TimeSpan.FromMinutes(1));
    Assert.Equal(1, await f.Worker.RunOnceAsync(default));

    Assert.Equal(
      MessageAttachmentStates.Stored,
      (await f.Attachment(id)).State
    );
    Assert.Equal(before + 1, await f.RevisionAsync());
    Assert.True(listening.Reader.TryRead(out _));
    Assert.False(listening.Reader.TryRead(out _));
  }

  // Another pass took the attachment over while this one was copying: this
  // pass's outcome, revision and signal are all dropped.
  [Fact]
  public async Task APassThatLostItsLeaseChangesAndSignalsNothing()
  {
    await using var f = await Fixture.CreateAsync();
    f.Messaging.Media["1234567890"] = (Pdf, "application/pdf", null);
    var id = await f.ReceiveAsync("");
    var before = await f.RevisionAsync();
    var usurper = Guid.NewGuid();
    f.Messaging.DuringMedia = () =>
      f
        .Db.MessageAttachments.Where(x => x.Id == id)
        .ExecuteUpdateAsync(x => x.SetProperty(a => a.LeaseToken, usurper));
    using var listening = f.Events.Subscribe(Company.Amf);

    Assert.Equal(0, await f.Worker.RunOnceAsync(default));

    var attachment = await f.Attachment(id);
    Assert.Equal(
      (MessageAttachmentStates.Pending, (Guid?)usurper),
      (attachment.State, attachment.LeaseToken)
    );
    Assert.Equal(before, await f.RevisionAsync());
    Assert.False(listening.Reader.TryRead(out _));
  }

  // The file store's own finish was lost to another upload attempt, so the
  // file is still uploading: the attachment is not stored, and is tried
  // again until the file is complete.
  [Fact]
  public async Task AFileStillUploadingIsNotStoredYet()
  {
    await using var f = await Fixture.CreateAsync(
      (db, clock) => new TakenOverStorage(db, clock)
    );
    f.Messaging.Media["1234567890"] = (Pdf, "application/pdf", null);
    var id = await f.ReceiveAsync("");

    await f.Worker.RunOnceAsync(default);
    Assert.Equal(
      MessageAttachmentStates.Pending,
      (await f.Attachment(id)).State
    );
    Assert.Equal(
      StoredFileStates.Uploading,
      (await f.Db.StoredFiles.AsNoTracking().SingleAsync()).State
    );

    f.Clock.Advance(TimeSpan.FromHours(2));
    await f.Worker.RunOnceAsync(default);
    Assert.Equal(
      MessageAttachmentStates.Stored,
      (await f.Attachment(id)).State
    );
  }

  [Theory]
  [InlineData("", "image/jpeg", "Photo 2026.09.21 12.30.05.jpg")]
  [InlineData(
    "",
    "audio/ogg; codecs=opus",
    "Voice note 2026.09.21 12.30.05.ogg"
  )]
  [InlineData("scan.PDF", "application/pdf", "scan.PDF")]
  [InlineData("", "application/x-unknown", "File 2026.09.21 12.30.05")]
  public void AFileIsNamedForWhatItIsAndWhenItCameNeverAGuess(
    string original,
    string type,
    string name
  ) =>
    Assert.Equal(
      name,
      InboundMediaOperation.FileName(
        new MessageAttachment { OriginalName = original },
        type,
        new DateTime(2026, 9, 21, 12, 30, 5, DateTimeKind.Utc)
      )
    );

  private sealed class Fixture : IAsyncDisposable
  {
    private DispatchSyncFixture sync = null!;
    private ServiceProvider services = null!;
    public FakeDriverMessaging Messaging { get; } = new();
    public MessagingEvents Events { get; } = new();
    public ManualTimeProvider Clock { get; } =
      new(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    public AppDbContext Db => sync.Db;
    public InboundMediaOperation Worker =>
      new(
        services.GetRequiredService<IServiceScopeFactory>(),
        Events,
        Clock,
        NullLogger<InboundMediaOperation>.Instance
      );

    public List<IInterceptor> Interceptors { get; } = [];

    public static async Task<Fixture> CreateAsync(
      Func<AppDbContext, ManualTimeProvider, IFileStorageProvider>? provider =
        null
    )
    {
      var f = new Fixture { sync = await DispatchSyncFixture.CreateAsync() };
      var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(
          new Dictionary<string, string?>
          {
            ["Storage:Managed:Provider"] = "database",
          }
        )
        .Build();
      var collection = new ServiceCollection();
      collection.AddScoped(_ => f.sync.NewContext([.. f.Interceptors]));
      collection.AddScoped<IAppDbContext>(sp =>
        sp.GetRequiredService<AppDbContext>()
      );
      collection.AddSingleton<IConfiguration>(configuration);
      collection.AddSingleton<IDriverMessaging>(f.Messaging);
      collection.AddSingleton<ICurrentCompany>(new TestCompany());
      collection.AddSingleton<TimeProvider>(f.Clock);
      collection.AddSingleton<IStorageSecrets>(
        new StorageSecrets(new EphemeralDataProtectionProvider())
      );
      collection.AddSingleton(Options.Create(new StorageOptions()));
      collection.AddSingleton<StorageUploadGate>();
      if (provider is null)
        collection.AddScoped<IFileStorageProvider, DatabaseFileStorage>();
      else
        collection.AddScoped(sp =>
          provider(sp.GetRequiredService<AppDbContext>(), f.Clock)
        );
      collection.AddScoped<StorageTargets>();
      collection.AddScoped<FileStore>();
      collection.AddScoped<StoredFileCheck>();
      collection.AddScoped<StorageLayouts>();
      f.services = collection.BuildServiceProvider();
      return f;
    }

    // A driver's file arrives at 12:30:05 on 2026-09-21.
    public async Task<Guid> ReceiveAsync(string fileName, string id = "wamid.1")
    {
      var recorder = new InboxRecorder(Db, Clock);
      await recorder.RecordAsync(
        DriverMessageChannels.WhatsApp,
        "123456",
        [
          new DriverMessageInboundEvent(
            "+15558234327",
            new DateTime(2026, 9, 21, 12, 30, 5, DateTimeKind.Utc)
          )
          {
            ProviderMessageId = id,
            Kind = "file",
            Media = new("1234567890", "application/pdf", null, fileName),
          },
        ],
        default
      );
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
      var message = await Db
        .ConversationMessages.AsNoTracking()
        .SingleAsync(x => x.ProviderMessageId == id);
      return await Db
        .MessageAttachments.AsNoTracking()
        .Where(x => x.MessageId == message.Id)
        .Select(x => x.Id)
        .SingleAsync();
    }

    public Task<long> RevisionAsync() =>
      Db.Conversations.AsNoTracking().Select(x => x.Revision).SingleAsync();

    public Task<MessageAttachment> Attachment(Guid id)
    {
      Db.ChangeTracker.Clear();
      return Db.MessageAttachments.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    public async ValueTask DisposeAsync()
    {
      await services.DisposeAsync();
      await sync.DisposeAsync();
    }
  }

  private sealed class FailOnConversationUpdate : DbCommandInterceptor
  {
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    ) =>
      command.CommandText.Contains("UPDATE \"Conversations\"")
        ? throw new InvalidOperationException("Simulated crash.")
        : ValueTask.FromResult(result);
  }

  // The database store, where another upload attempt takes the file over
  // while this one is writing it, the first time only.
  private sealed class TakenOverStorage(
    AppDbContext db,
    ManualTimeProvider clock
  ) : IFileStorageProvider
  {
    private static int takenOver;
    private readonly DatabaseFileStorage inner = new(
      db,
      new ConfigurationBuilder()
        .AddInMemoryCollection(
          new Dictionary<string, string?>
          {
            ["Storage:Managed:Provider"] = "database",
          }
        )
        .Build()
    );
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
      await inner.PutAsync(target, key, upload, ct);
      if (Interlocked.Exchange(ref takenOver, 1) == 0)
      {
        var until = clock.GetUtcNow().UtcDateTime.AddMinutes(30);
        await db
          .StoredFiles.Where(x => x.Id == upload.FileId)
          .ExecuteUpdateAsync(
            x =>
              x.SetProperty(f => f.UploadToken, Guid.NewGuid())
                .SetProperty(f => f.UploadLeaseUntil, until),
            ct
          );
      }
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
    ) => inner.DeleteAsync(target, key, ct);

    public Task<bool> CheckAsync(StorageTarget target, CancellationToken ct) =>
      Task.FromResult(true);
  }
}
