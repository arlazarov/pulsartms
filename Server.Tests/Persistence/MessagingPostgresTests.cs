using System.Data.Common;
using Application.Diagnostics.Consistency;
using Application.Features.Dispatch.Audit;
using Application.Features.Dispatch.Documents;
using Application.Features.Messaging.Audit;
using Application.Features.Messaging.Services;
using Application.Interfaces;
using Application.Storage;
using Domain.Entities;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;
using Domain.Models.Messaging;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Persistence;

// What this release's messaging and filing rely on that only PostgreSQL
// does: the migrations as production applies them, with their backfills
// over rows that already exist; the read-marker upsert; the arrival head's
// optimistic check between two real connections; the filing's row claim
// against a quarantine committed from another connection; and the new
// auditor queries executed, not just translated.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class MessagingPostgresTests
{
  private const string BeforeRevisions =
    "20260923200848_AddConversationTemplates";

  [RequiresPostgresFact]
  public async Task TheMigrationsApplyAndBackfillArrivalsOverExistingRows()
  {
    await using var fixture = await PostgresFixture.CreateAsync(empty: true);
    var db = fixture.Connect();
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync(BeforeRevisions);
    var conversation = Guid.NewGuid();
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "Conversations" ("Id", "CompanyId", "Channel",
        "BusinessNumberId", "Participant", "LastInboundAt", "LastMessageAt",
        "LastPreview", "Revision")
      VALUES ({conversation}, {Company.Amf}, 'whatsapp', '123456',
        '+15550000001', now(), now(), 'Loaded.', 3);
      INSERT INTO "ConversationMessages" ("Id", "CompanyId", "ConversationId",
        "Channel", "BusinessNumberId", "Direction", "Kind", "Body", "Attempt",
        "Status", "StatusAt", "SentAt", "CreatedAt")
      VALUES
        ({Guid.NewGuid()}, {Company.Amf}, {conversation}, 'whatsapp',
          '123456', 'in', 'text', 'Loaded.', 0, 'received', now(), now(),
          now()),
        ({Guid.NewGuid()}, {Company.Amf}, {conversation}, 'whatsapp',
          '123456', 'out', 'text', 'Thanks.', 1, 'sent', now(), now(), now());
      INSERT INTO "ConversationReads" ("Id", "CompanyId", "ConversationId",
        "UserId", "ReadThrough")
      VALUES ({Guid.NewGuid()}, {Company.Amf}, {conversation},
        {Guid.NewGuid()}, now());
      """
    );

    await migrator.MigrateAsync();
    await db.DisposeAsync();
    db = fixture.Connect();

    Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    var row = await db
      .Conversations.IgnoreQueryFilters()
      .AsNoTracking()
      .SingleAsync();
    Assert.Equal((3, 1), (row.LastInboundRevision, row.LastInboundSequence));
    Assert.Equal(
      [("in", 3L), ("out", 0L)],
      (
        await db
          .ConversationMessages.IgnoreQueryFilters()
          .AsNoTracking()
          .OrderBy(x => x.Direction)
          .Select(x => new { x.Direction, x.ArrivedRevision })
          .ToListAsync()
      ).Select(x => (x.Direction, x.ArrivedRevision))
    );
    Assert.Equal(
      1,
      (
        await db
          .ConversationArrivalHeads.IgnoreQueryFilters()
          .AsNoTracking()
          .SingleAsync()
      ).Sequence
    );
    // The old time-based marker is not carried over: unread once.
    Assert.Equal(
      0,
      (
        await db
          .ConversationReads.IgnoreQueryFilters()
          .AsNoTracking()
          .SingleAsync()
      ).ReadRevision
    );
  }

  [RequiresPostgresFact]
  public async Task TheReadMarkerKeepsTheHighestOfManyConcurrentWriters()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var conversation = await ConversationAsync(fixture.Connect());
    var user = Guid.NewGuid();
    int[] revisions = [7, 10, 3, 9, 10, 1, 8, 2, 6, 4];

    await Task.WhenAll(
      revisions.Select(async revision =>
      {
        await using var db = fixture.Connect();
        await new ConversationReadMarkers(db).AdvanceAsync(
          Company.Amf,
          conversation,
          user,
          revision,
          default
        );
      })
    );
    await using (var late = fixture.Connect())
      await new ConversationReadMarkers(late).AdvanceAsync(
        Company.Amf,
        conversation,
        user,
        5,
        default
      );

    var reads = await fixture
      .Connect()
      .ConversationReads.AsNoTracking()
      .ToListAsync();
    Assert.Equal(10, Assert.Single(reads).ReadRevision);
  }

  [RequiresPostgresFact]
  public async Task TwoRecordingsOfOneCompanyCommitTheirArrivalsInOrder()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    await using (var seed = fixture.Connect())
    {
      await RecordAsync(seed, "+15550000001");
      await seed.SaveChangesAsync();
    }
    await using var first = fixture.Connect();
    await using var second = fixture.Connect();

    await RecordAsync(first, "+15550000002");
    await RecordAsync(second, "+15550000003");
    await first.SaveChangesAsync();
    var conflict = await Record.ExceptionAsync(() => second.SaveChangesAsync());

    Assert.True(second.IsWriteConflict(conflict!), conflict?.ToString());
    await using var retry = fixture.Connect();
    await RecordAsync(retry, "+15550000003");
    await retry.SaveChangesAsync();
    var sequences = await fixture
      .Connect()
      .Conversations.AsNoTracking()
      .OrderBy(x => x.LastInboundSequence)
      .Select(x => new { x.Participant, x.LastInboundSequence })
      .ToListAsync();
    Assert.Equal(
      [("+15550000001", 1L), ("+15550000002", 2L), ("+15550000003", 3L)],
      sequences.Select(x => (x.Participant, x.LastInboundSequence))
    );
  }

  // Another connection quarantines the file and commits after the filing
  // read it as released. The filing's claim on the file's row sees it, or
  // the serializable commit refuses; either way nothing is filed.
  [RequiresPostgresFact]
  public async Task AQuarantineCommittedAfterTheCheckStopsTheFiling()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var (attachment, load, user) = await FileableAsync(fixture.Connect());
    var pause = new QuarantineAfterCheck(fixture);
    await using var db = fixture.Connect(pause);

    var result = await new FileMessageAttachmentHandler(
      db,
      new Caller(user),
      new DispatchRole(),
      TimeProvider.System
    ).Handle(new(attachment, load, null, "bol"), default);

    Assert.True(pause.Committed);
    Assert.Equal(409, result.StatusCode);
    Assert.Empty(
      await fixture.Connect().DispatchDocuments.AsNoTracking().ToListAsync()
    );
  }

  [RequiresPostgresFact]
  public async Task TheNewAuditorReadsRunOnPostgres()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect();
    var request = new ConsistencyPageRequest(
      Company.Amf,
      DateTime.UtcNow,
      Guid.NewGuid().ToString(),
      10,
      TimeSpan.FromMinutes(30)
    );

    Assert.Empty(
      (await new UnreadArrivalRule(db).ReadAsync(request, default)).Observed
    );
    Assert.Empty(
      (await new FiledDocumentRule(db).ReadAsync(request, default)).Observed
    );
  }

  private static async Task<Guid> ConversationAsync(AppDbContext db)
  {
    await RecordAsync(db, "+15550000001");
    await db.SaveChangesAsync();
    return await db.Conversations.Select(x => x.Id).SingleAsync();
  }

  private static Task RecordAsync(AppDbContext db, string phone) =>
    new InboxRecorder(db, TimeProvider.System).RecordAsync(
      DriverMessageChannels.WhatsApp,
      "123456",
      [
        new DriverMessageInboundEvent(phone, DateTime.UtcNow)
        {
          ProviderMessageId = $"wamid.{phone}",
          Text = "Loaded.",
        },
      ],
      default
    );

  // A released PDF on a driver's message, a load and a dispatcher.
  private static async Task<(
    Guid Attachment,
    Guid Load,
    string User
  )> FileableAsync(AppDbContext db)
  {
    await RecordAsync(db, "+15550000001");
    var connection = new StorageConnection
    {
      Id = Guid.NewGuid(),
      Kind = StorageKinds.Managed,
      DisplayName = "PulsR storage",
      State = StorageConnectionStates.Connected,
      CreatedAt = DateTime.UtcNow,
    };
    var file = new StoredFile
    {
      Id = Guid.NewGuid(),
      ConnectionId = connection.Id,
      ObjectKey = Guid.NewGuid().ToString("N"),
      ContentType = "application/pdf",
      Name = "bol.pdf",
      Size = 12,
      Sha256 = new string('a', 64),
      State = StoredFileStates.Available,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow,
    };
    var load = new DispatchEntity { Id = Guid.NewGuid(), LoadNumber = 1441 };
    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "filing-dispatcher",
      Name = "Dispatcher",
      Email = "dispatcher@example.invalid",
    };
    db.AddRange(connection, file, load, user);
    await db.SaveChangesAsync();
    var message = await db.ConversationMessages.Select(x => x.Id).SingleAsync();
    var attachment = new MessageAttachment
    {
      Id = Guid.NewGuid(),
      MessageId = message,
      StoredFileId = file.Id,
      OriginalName = "bol.pdf",
      DeclaredType = "application/pdf",
      State = "stored",
      CreatedAt = DateTime.UtcNow,
    };
    db.MessageAttachments.Add(attachment);
    await db.SaveChangesAsync();
    return (attachment.Id, load.Id, user.IdentityUserId);
  }

  // After the filing's read of the attachment and its stored file, another
  // connection quarantines the file and commits.
  private sealed class QuarantineAfterCheck(PostgresFixture fixture)
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
        || !command.CommandText.Contains("\"MessageAttachments\"")
        || !command.CommandText.Contains("\"StoredFiles\"")
      )
        return result;
      Committed = true;
      await using var other = fixture.Connect();
      await other.StoredFiles.ExecuteUpdateAsync(
        x => x.SetProperty(f => f.State, StoredFileStates.Quarantined),
        cancellationToken
      );
      return result;
    }
  }

  private sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => identity;
  }

  private sealed class DispatchRole : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<string?>("Dispatch");

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string role,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
