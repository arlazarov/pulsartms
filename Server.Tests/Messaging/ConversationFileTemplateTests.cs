using System.Security.Cryptography;
using System.Text;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Options;
using Application.Features.Messaging.Queries;
using Application.Features.Messaging.Services;
using Application.Models;
using Application.Storage;
using Domain.Entities.Messaging;
using Domain.Entities.Storage;
using Domain.Models.Messaging;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Storage;

namespace Server.Tests.Messaging;

// Files and templates out: a dispatcher's file is stored under its retry
// key, checked, and sent once through the same fenced outbox; a file that
// is not what it says is refused before anything is queued; only approved
// templates go, and they may go outside the driver's window.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class ConversationFileTemplateTests
{
  private static readonly byte[] Pdf = Encoding.UTF8.GetBytes("%PDF-1.7 rate");

  [Fact]
  public async Task AFileIsStoredCheckedAndSentOnce()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var key = Guid.NewGuid();

    var queued = await SendAsync(
      f,
      conversation,
      key,
      last,
      Pdf,
      "application/pdf"
    );
    var again = await SendAsync(
      f,
      conversation,
      key,
      last,
      Pdf,
      "application/pdf"
    );
    Assert.True(queued.Success, string.Join(";", queued.Errors ?? []));
    Assert.Equal(queued.Response!.Id, again.Response!.Id);

    Assert.Equal(1, await f.Worker.RunOnceAsync(default));

    var sent = Assert.Single(f.Messaging.Files);
    Assert.Equal(("rate.pdf", "Signed"), (sent.Name, sent.Caption));
    Assert.Equal(Pdf, sent.Bytes);
    var file = await f.Db.StoredFiles.AsNoTracking().SingleAsync();
    Assert.Equal((key, StoredFileStates.Available), (file.Id, file.State));
    Assert.StartsWith("Sent/", file.Folder);
  }

  [Fact]
  public async Task AFileThatIsNotWhatItSaysIsRefusedBeforeAnythingIsQueued()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var page = Encoding.UTF8.GetBytes("<html>not a pdf</html>");

    var refused = await SendAsync(
      f,
      conversation,
      Guid.NewGuid(),
      last,
      page,
      "application/pdf"
    );

    Assert.Equal(400, refused.StatusCode);
    Assert.Empty(
      await f
        .Db.ConversationMessages.Where(x => x.Direction == "out")
        .ToListAsync()
    );
    Assert.Equal(
      StoredFileStates.Rejected,
      (await f.Db.StoredFiles.AsNoTracking().SingleAsync()).State
    );
  }

  // The file was queued, then its stored copy went away before sending:
  // the reply is withdrawn without calling the provider.
  [Fact]
  public async Task AFileThatCanNoLongerBeReadIsWithdrawnWithoutACall()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var queued = (
      await SendAsync(
        f,
        conversation,
        Guid.NewGuid(),
        last,
        Pdf,
        "application/pdf"
      )
    ).Response!;
    await f.Db.ManagedFileBlobs.ExecuteDeleteAsync();

    await f.Worker.RunOnceAsync(default);

    Assert.Equal(
      DriverMessageStatuses.Withdrawn,
      (await f.MessageAsync(queued.Id)).Status
    );
    Assert.Empty(f.Messaging.Files);
  }

  [Fact]
  public async Task OnlyAnApprovedTemplateGoesAndItMayGoOutsideTheWindow()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, _) = await f.ConversationAsync(hoursAgo: 30);
    MessageTemplate[] approved =
    [
      new()
      {
        Name = "fuel_plan_ready",
        Language = "en_US",
        Parameters = 1,
        Text = "Your fuel plan for load {{1}} is ready. Reply to receive it.",
      },
    ];
    var key = Guid.NewGuid();

    Assert.Equal(
      400,
      (
        await f.Files(approved)
          .Handle(
            new SendConversationTemplateCommand(
              conversation,
              key,
              "made_up",
              "en_US",
              ["1407"]
            ),
            default
          )
      ).StatusCode
    );
    Assert.Equal(
      400,
      (
        await f.Files(approved)
          .Handle(
            new SendConversationTemplateCommand(
              conversation,
              key,
              "fuel_plan_ready",
              "en_US",
              []
            ),
            default
          )
      ).StatusCode
    );
    var queued = await f.Files(approved)
      .Handle(
        new SendConversationTemplateCommand(
          conversation,
          key,
          "fuel_plan_ready",
          "en_US",
          ["1407"]
        ),
        default
      );
    var again = await f.Files(approved)
      .Handle(
        new SendConversationTemplateCommand(
          conversation,
          key,
          "fuel_plan_ready",
          "en_US",
          ["1407"]
        ),
        default
      );

    Assert.Equal(
      "Your fuel plan for load 1407 is ready. Reply to receive it.",
      queued.Response!.Body
    );
    Assert.Equal(queued.Response.Id, again.Response!.Id);
    await f.Worker.RunOnceAsync(default);
    var template = Assert.Single(f.Messaging.Templates);
    Assert.Equal(
      ("+15558234327", "fuel_plan_ready", "1407"),
      (template.To, template.Name, Assert.Single(template.Parameters))
    );
    Assert.Equal(
      DriverMessageStatuses.Accepted,
      (await f.MessageAsync(queued.Response.Id)).Status
    );
    Assert.Empty(
      (
        await f.Files().Handle(new GetMessageTemplatesQuery(), default)
      ).Response!
    );
  }

  [Fact]
  public async Task OnlyACheckedFileIsServed()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, last) = await f.ConversationAsync();
    var queued = (
      await SendAsync(
        f,
        conversation,
        Guid.NewGuid(),
        last,
        Pdf,
        "application/pdf"
      )
    ).Response!;
    var attachment = await f
      .Db.MessageAttachments.AsNoTracking()
      .SingleAsync(x => x.MessageId == queued.Id);
    var handler = new AttachmentContentHandler(f.Db, FileStorage(f));

    var served = await handler.Handle(
      new GetAttachmentContentQuery(attachment.Id),
      default
    );
    await using (served.Response!.Content)
      Assert.Equal("application/pdf", served.Response.ContentType);

    await f.Db.StoredFiles.ExecuteUpdateAsync(x =>
      x.SetProperty(s => s.State, StoredFileStates.Quarantined)
    );
    Assert.Equal(
      404,
      (
        await handler.Handle(
          new GetAttachmentContentQuery(attachment.Id),
          default
        )
      ).StatusCode
    );
  }

  private static FileStore FileStorage(ReplyFixture f) =>
    FileStorageTests.Store(f.Db, clock: f.Clock);

  private static Task<RequestResponse<MessageView>> SendAsync(
    ReplyFixture f,
    Guid conversation,
    Guid key,
    Guid last,
    byte[] bytes,
    string type
  ) =>
    f.Files()
      .Handle(
        new SendConversationFileCommand(
          conversation,
          key,
          "rate.pdf",
          type,
          bytes.Length,
          Convert.ToHexStringLower(SHA256.HashData(bytes)),
          new MemoryStream(bytes),
          "Signed",
          last,
          false
        ),
        default
      );
}
