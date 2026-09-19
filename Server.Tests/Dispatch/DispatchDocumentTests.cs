using Application.Features.Dispatch.Documents;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchDocumentTests
{
  [Theory]
  [InlineData("rename")]
  [InlineData("disable")]
  [InlineData("delete")]
  public async Task HistoricalAuthorAndDocumentSurviveAccountChanges(
    string change
  )
  {
    await using var f = await DispatchDocumentFixture.CreateAsync();
    var uploader = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "historical-uploader",
      Name = "Original uploader",
      Email = "history@example.invalid",
    };
    f.Db.Users.Add(uploader);
    await f.Db.SaveChangesAsync();
    var command = f.Command();
    var uploaded = await f.Upload(identity: uploader.IdentityUserId)
      .Handle(command, default);
    Assert.True(uploaded.Success);
    if (change == "rename")
      uploader.Name = "Renamed uploader";
    else if (change == "disable")
      uploader.IsActive = false;
    else
      f.Db.Users.Remove(uploader);
    await f.Db.SaveChangesAsync();
    if (change == "rename")
      Assert.Equal(
        uploaded.Response,
        (
          await f.Upload(identity: uploader.IdentityUserId)
            .Handle(command, default)
        ).Response
      );
    f.Db.ChangeTracker.Clear();
    var listed = await f.Read().Handle(new(f.Load.Id), default);
    var document = Assert.Single(listed.Response!);
    Assert.Equal(uploaded.Response, document);
    Assert.Equal("Original uploader", document.ActorName);
    var download = await f.Download()
      .Handle(new(f.Load.Id, document.Id), default);
    Assert.Equal(command.Request.Content, download.Response!.Content);
  }

  [Fact]
  public async Task UploadRetryIsIdempotentAndRejectsChangedPayloadOrActor()
  {
    await using var f = await DispatchDocumentFixture.CreateAsync();
    var command = f.Command();
    var uploaded = await f.Upload().Handle(command, default);
    Assert.True(uploaded.Success);
    Assert.Equal(f.Actor.Name, uploaded.Response!.ActorName);
    Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, uploaded.Response.RecordedAt);
    f.Commands.Clear();
    var repeated = await f.Upload().Handle(command, default);
    Assert.Equal(uploaded.Response, repeated.Response);
    Assert.DoesNotContain(f.Commands, sql => sql.Contains(".\"Content\""));
    Assert.Equal(1, await f.Db.DispatchDocuments.CountAsync());
    Assert.Equal(
      409,
      (
        await f.Upload()
          .Handle(
            command with
            {
              Request = command.Request with { Kind = "bol" },
            },
            default
          )
      ).StatusCode
    );
    Assert.Equal(
      409,
      (
        await f.Upload()
          .Handle(
            command with
            {
              Request = command.Request with
              {
                Content = "%PDF-other payload"u8.ToArray(),
              },
            },
            default
          )
      ).StatusCode
    );
    var other = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "other-operator",
      Name = "Other operator",
      Email = "other@example.invalid",
    };
    f.Db.Users.Add(other);
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      409,
      (
        await f.Upload(identity: other.IdentityUserId).Handle(command, default)
      ).StatusCode
    );
    Assert.Equal(1, await f.Db.DispatchDocuments.CountAsync());
  }

  [Fact]
  public async Task ListProjectsMetadataOnlyAndDownloadRequiresExactLoad()
  {
    await using var f = await DispatchDocumentFixture.CreateAsync();
    var command = f.Command();
    var saved = (await f.Upload().Handle(command, default)).Response!;
    var other = new DispatchEntity { Id = Guid.NewGuid(), LoadNumber = 1051 };
    f.Db.Dispatches.Add(other);
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      409,
      (
        await f.Upload().Handle(command with { DispatchId = other.Id }, default)
      ).StatusCode
    );
    f.Commands.Clear();
    var listed = await f.Read().Handle(new(f.Load.Id), default);
    Assert.Equal(saved.Id, Assert.Single(listed.Response!).Id);
    Assert.DoesNotContain(f.Commands, sql => sql.Contains(".\"Content\""));
    Assert.Empty((await f.Read().Handle(new(other.Id), default)).Response!);
    Assert.Equal(
      404,
      (await f.Download().Handle(new(other.Id, saved.Id), default)).StatusCode
    );
    var downloaded = await f.Download()
      .Handle(new(f.Load.Id, saved.Id), default);
    Assert.Equal(command.Request.Content, downloaded.Response!.Content);
    Assert.Equal("application/pdf", downloaded.Response.ContentType);
  }

  [Theory]
  [InlineData("Driver")]
  [InlineData(null)]
  public async Task UnauthorizedRolesCannotAccessFiles(string? role)
  {
    await using var f = await DispatchDocumentFixture.CreateAsync();
    Assert.Equal(
      403,
      (await f.Upload(role).Handle(f.Command(), default)).StatusCode
    );
    Assert.Equal(
      403,
      (await f.Read(role).Handle(new(f.Load.Id), default)).StatusCode
    );
    Assert.Equal(
      403,
      (
        await f.Download(role).Handle(new(f.Load.Id, Guid.NewGuid()), default)
      ).StatusCode
    );
    f.Actor.IsActive = false;
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      403,
      (await f.Upload().Handle(f.Command(), default)).StatusCode
    );
    Assert.Equal(
      403,
      (await f.Upload(identity: null).Handle(f.Command(), default)).StatusCode
    );
    Assert.Empty(f.Db.DispatchDocuments);
  }

  [Fact]
  public async Task RejectsBadContentMissingLoadsAndTheDocumentCountLimit()
  {
    await using var f = await DispatchDocumentFixture.CreateAsync();
    var command = f.Command();
    Assert.Equal(
      400,
      (
        await f.Upload()
          .Handle(
            command with
            {
              Request = command.Request with
              {
                Content = "<html>bad file</html>"u8.ToArray(),
              },
            },
            default
          )
      ).StatusCode
    );
    Assert.Equal(
      404,
      (
        await f.Upload()
          .Handle(command with { DispatchId = Guid.NewGuid() }, default)
      ).StatusCode
    );
    f.Db.DispatchDocuments.AddRange(
      Enumerable
        .Range(0, 50)
        .Select(_ => new DispatchDocument
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Load.Id,
          RecordedBy = f.Actor.Id,
          Content = command.Request.Content,
          ContentType = "application/pdf",
          FileName = "fixture.pdf",
          Length = command.Request.Content.Length,
        })
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal(409, (await f.Upload().Handle(command, default)).StatusCode);
    Assert.Equal(50, await f.Db.DispatchDocuments.CountAsync());
  }
}
