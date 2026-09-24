using System.Security.Cryptography;
using System.Text;
using Application.Features.Messaging.Commands;
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
    ApprovedTemplate[] approved =
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
    Assert.Equal(
      "fuel_plan_ready",
      Assert
        .Single(
          (
            await f.Files().Handle(new GetMessageTemplatesQuery(), default)
          ).Response!
        )
        .Name
    );
  }

  // A template approved for another carrier, or for another of this
  // carrier's numbers, is not this conversation's: it is neither offered
  // nor queued.
  [Fact]
  public async Task ATemplateIsOnlyItsOwnCarriersAndNumbers()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, _) = await f.ConversationAsync(hoursAgo: 30);
    f.Db.ApprovedTemplates.AddRange(
      Template("theirs", company: Guid.NewGuid()),
      Template("old_number", number: "999999")
    );
    await f.Db.SaveChangesAsync();

    Assert.Empty(
      (
        await f.Files().Handle(new GetMessageTemplatesQuery(), default)
      ).Response!
    );
    foreach (var name in new[] { "theirs", "old_number" })
      Assert.Equal(
        400,
        (await QueueTemplateAsync(f, conversation, name)).StatusCode
      );
    Assert.Empty(
      await f
        .Db.ConversationMessages.AsNoTracking()
        .Where(x => x.Kind == ConversationMessageKinds.Template)
        .ToListAsync()
    );
  }

  // Queued while approved; withdrawn, or the carrier moved to another
  // number, before the worker's turn: nothing is sent. A conversation on a
  // number the carrier no longer sends from queues nothing.
  [Theory]
  [InlineData("withdrawn")]
  [InlineData("number")]
  public async Task ATemplateNoLongerApprovedAtTheSendIsNotSent(string cause)
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, _) = await f.ConversationAsync(hoursAgo: 30);
    f.Db.ApprovedTemplates.Add(Template("fuel_plan_ready"));
    await f.Db.SaveChangesAsync();
    var queued = (
      await QueueTemplateAsync(f, conversation, "fuel_plan_ready")
    ).Response!;

    if (cause == "withdrawn")
      await f.Db.ApprovedTemplates.ExecuteDeleteAsync();
    else
      f.Messaging.BusinessNumber = "999999";
    await f.Worker.RunOnceAsync(default);

    Assert.Equal(
      DriverMessageStatuses.Withdrawn,
      (await f.MessageAsync(queued.Id)).Status
    );
    Assert.Empty(f.Messaging.Templates);
    if (cause == "number")
      Assert.Equal(
        409,
        (
          await QueueTemplateAsync(f, conversation, "fuel_plan_ready")
        ).StatusCode
      );
  }

  private static ApprovedTemplate Template(
    string name,
    Guid? company = null,
    string number = "123456"
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      CompanyId = company ?? Domain.Entities.Company.Amf,
      Channel = DriverMessageChannels.WhatsApp,
      BusinessNumberId = number,
      Name = name,
      Language = "en_US",
      Parameters = 0,
      Text = "Your fuel plan is ready.",
    };

  private static Task<RequestResponse<MessageView>> QueueTemplateAsync(
    ReplyFixture f,
    Guid conversation,
    string name
  ) =>
    f.Files()
      .Handle(
        new SendConversationTemplateCommand(
          conversation,
          Guid.NewGuid(),
          name,
          "en_US",
          []
        ),
        default
      );

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
