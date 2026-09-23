using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Application.Diagnostics.Consistency;
using Application.Features.Dispatch.Audit;
using Application.Features.Dispatch.Documents;
using Application.Features.Routing.Commands;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Tests.Storage;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

// A file a driver sent becomes a load document only when a dispatcher
// files it: once, by reference to the stored file, and only while that
// file is released - checked inside the committing transaction, where the
// file's row is claimed. The download refuses a file that is no longer
// released or no longer what was recorded.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class MessageFilingTests
{
  private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 bol");

  [Fact]
  public async Task FilingTwiceFilesOnceByReferenceAndServesTheSameBytes()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (attachment, load) = await FileableAsync(f);

    var first = await File(f)
      .Handle(new(attachment, load, null, "bol"), default);
    var again = await File(f)
      .Handle(new(attachment, load, null, "bol"), default);

    Assert.True(first.Success, string.Join(";", first.Errors ?? []));
    Assert.Equal(first.Response!.Id, again.Response!.Id);
    var document = await f.Db.DispatchDocuments.AsNoTracking().SingleAsync();
    Assert.Empty(document.Content);
    Assert.NotNull(document.StoredFileId);
    var served = await Download(f).Handle(new(load, document.Id), default);
    Assert.Equal(Pdf, served.Response!.Content);
  }

  [Fact]
  public async Task OnlyAReleasedFileOnThisCompanysLoadIsFiled()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (attachment, load) = await FileableAsync(f);
    var foreign = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      CompanyId = Guid.NewGuid(),
      LoadNumber = 1407,
    };
    f.Db.Dispatches.Add(foreign);
    await f.Db.SaveChangesAsync();

    Assert.Equal(
      404,
      (
        await File(f).Handle(new(attachment, foreign.Id, null, "bol"), default)
      ).StatusCode
    );
    Assert.Equal(
      400,
      (
        await File(f).Handle(new(attachment, load, null, "photo"), default)
      ).StatusCode
    );
    await f.Db.StoredFiles.ExecuteUpdateAsync(x =>
      x.SetProperty(s => s.State, StoredFileStates.Quarantined)
    );
    Assert.Equal(
      409,
      (
        await File(f).Handle(new(attachment, load, null, "bol"), default)
      ).StatusCode
    );
    Assert.Empty(await f.Db.DispatchDocuments.ToListAsync());
  }

  [Fact]
  public async Task ALoadIsFiledByItsNumberAndAnUnknownNumberIsNot()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (attachment, load) = await FileableAsync(f);

    var byNumber = await File(f)
      .Handle(new(attachment, null, 1441, "pod"), default);
    Assert.True(byNumber.Success);
    Assert.Equal(
      load,
      (await f.Db.DispatchDocuments.AsNoTracking().SingleAsync()).DispatchId
    );

    Assert.Equal(
      404,
      (
        await File(f).Handle(new(attachment, null, 7, "pod"), default)
      ).StatusCode
    );
  }

  // The file is quarantined after the filing read it as released and
  // before the filing commits. The claim on the file's row, made in the
  // same transaction, sees the change and nothing is filed.
  [Fact]
  public async Task AFileQuarantinedAfterTheCheckIsNotFiled()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (attachment, load) = await FileableAsync(f);
    var pause = new AfterCheck();
    await using var db = f.Context(pause);

    var result = await new FileMessageAttachmentHandler(
      db,
      new ReplyFixture.Caller("me"),
      new Roles(),
      f.Clock
    ).Handle(new(attachment, load, null, "bol"), default);

    Assert.True(pause.Changed);
    Assert.Equal(409, result.StatusCode);
    f.Db.ChangeTracker.Clear();
    Assert.Empty(await f.Db.DispatchDocuments.ToListAsync());
  }

  // Defense for a document already filed: a file quarantined later, or one
  // whose stored bytes are no longer what was recorded, is not served.
  [Fact]
  public async Task TheDownloadRefusesAFileThatChangedAfterFiling()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (attachment, load) = await FileableAsync(f);
    var document = (
      await File(f).Handle(new(attachment, load, null, "bol"), default)
    )
      .Response!
      .Id;

    await f.Db.ManagedFileBlobs.ExecuteUpdateAsync(x =>
      x.SetProperty(b => b.Content, [.. Pdf, .. "more"u8.ToArray()])
    );
    Assert.Equal(
      404,
      (await Download(f).Handle(new(load, document), default)).StatusCode
    );
    await f.Db.StoredFiles.ExecuteUpdateAsync(x =>
      x.SetProperty(s => s.State, StoredFileStates.Quarantined)
    );
    Assert.Equal(
      404,
      (await Download(f).Handle(new(load, document), default)).StatusCode
    );
  }

  // The auditor finds a filed document whose stored file is no longer
  // released, and nothing while it is.
  [Fact]
  public async Task TheAuditorFindsAFiledDocumentThatCannotBeOpened()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (attachment, load) = await FileableAsync(f);
    var document = (
      await File(f).Handle(new(attachment, load, null, "bol"), default)
    )
      .Response!
      .Id;
    var rule = new FiledDocumentRule(f.Db);
    ConsistencyPageRequest Page() =>
      new(Company.Amf, DateTime.UtcNow, null, 10, TimeSpan.FromMinutes(30));

    Assert.Empty((await rule.ReadAsync(Page(), default)).Observed);
    await f.Db.StoredFiles.ExecuteUpdateAsync(x =>
      x.SetProperty(s => s.State, StoredFileStates.Missing)
    );

    var found = Assert.Single((await rule.ReadAsync(Page(), default)).Observed);
    Assert.Equal(
      (document.ToString(), "file:missing"),
      (found.EntityKey, found.Versions)
    );
  }

  // A released PDF that came with a dispatcher's reply, and load AMF1441.
  private static async Task<(Guid Attachment, Guid Load)> FileableAsync(
    ReplyFixture f
  )
  {
    var (conversation, last) = await f.ConversationAsync();
    var sent = await f.Files()
      .Handle(
        new SendConversationFileCommand(
          conversation,
          Guid.NewGuid(),
          "bill.pdf",
          "application/pdf",
          Pdf.Length,
          Convert.ToHexStringLower(SHA256.HashData(Pdf)),
          new MemoryStream(Pdf),
          "",
          last,
          false
        ),
        default
      );
    Assert.True(sent.Success, string.Join(";", sent.Errors ?? []));
    var load = new DispatchEntity { Id = Guid.NewGuid(), LoadNumber = 1441 };
    f.Db.Dispatches.Add(load);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    var attachment = await f
      .Db.MessageAttachments.AsNoTracking()
      .Select(x => x.Id)
      .SingleAsync();
    return (attachment, load.Id);
  }

  private static FileMessageAttachmentHandler File(ReplyFixture f)
  {
    f.Db.ChangeTracker.Clear();
    return new(f.Db, new ReplyFixture.Caller("me"), new Roles(), f.Clock);
  }

  private static DownloadDispatchDocumentHandler Download(ReplyFixture f)
  {
    f.Db.ChangeTracker.Clear();
    return new(
      f.Db,
      new ReplyFixture.Caller("me"),
      new Roles(),
      FileStorageTests.Store(f.Db, clock: f.Clock)
    );
  }

  // After the filing's read of the attachment and its stored file, a
  // concurrent quarantine lands before anything else runs.
  private sealed class AfterCheck : DbCommandInterceptor
  {
    public bool Changed { get; private set; }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
      DbCommand command,
      CommandExecutedEventData eventData,
      DbDataReader result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        Changed
        || !command.CommandText.Contains("\"MessageAttachments\"")
        || !command.CommandText.Contains("\"StoredFiles\"")
      )
        return result;
      Changed = true;
      await using var change = command.Connection!.CreateCommand();
      change.Transaction = command.Transaction;
      change.CommandText =
        "UPDATE \"StoredFiles\" SET \"State\" = 'quarantined'";
      await change.ExecuteNonQueryAsync(cancellationToken);
      return result;
    }
  }

  private sealed class Roles : IUserRoleService
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
