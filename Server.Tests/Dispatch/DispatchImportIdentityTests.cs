using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchImportIdentityTests
{
  [Fact]
  public async Task ImportCannotTakeOverNativeLoadWithSameDisplayNumber()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var native = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = 10,
      OrderNumber = "Native",
      Status = "planned",
    };
    f.Db.Dispatches.Add(native);
    await f.Db.SaveChangesAsync();
    f.Sources.Add(
      new()
      {
        ExternalId = "remote-10",
        LoadNumber = 10,
        OrderNumber = "Imported",
      }
    );
    Assert.True((await f.Handler.Handle(new(), default)).Success);
    var link = await f
      .Db.DispatchSourceLinks.Include(x => x.Dispatch)
      .SingleAsync();
    Assert.NotEqual(native.Id, link.DispatchId);
    Assert.NotEqual(native.LoadNumber, link.Dispatch.LoadNumber);
    Assert.Equal("Native", native.OrderNumber);
    Assert.Equal("Imported", link.Dispatch.OrderNumber);
    Assert.True((await f.Handler.Handle(new(), default)).Success);
    Assert.Equal(2, await f.Db.Dispatches.CountAsync());
  }

  [Fact]
  public async Task SourceIdentitySurvivesChangedDisplayNumberAndRestart()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = new ExternalDispatch
    {
      ExternalId = "stable-key",
      LoadNumber = 12,
    };
    f.Sources.Add(source);
    Assert.True((await f.Handler.Handle(new(), default)).Success);
    var first = await f.Db.Dispatches.SingleAsync();
    var id = first.Id;
    source.LoadNumber = 99;
    source.OrderNumber = "Updated";
    f.Memory.Compact(1);
    f.Db.ChangeTracker.Clear();
    Assert.True((await f.Handler.Handle(new(), default)).Success);
    var second = await f.Db.Dispatches.SingleAsync();
    Assert.Equal(id, second.Id);
    Assert.Equal(12, second.LoadNumber);
    Assert.Equal("Updated", second.OrderNumber);
  }

  [Fact]
  public async Task SameExternalIdentityFromAnotherProviderCannotOverwrite()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var old = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = 12,
      OrderNumber = "Other",
    };
    f.Db.Dispatches.Add(old);
    f.Db.DispatchSourceLinks.Add(
      new DispatchSourceLink
      {
        Provider = "other",
        ExternalId = "shared",
        DisplayName = "Other",
        Dispatch = old,
      }
    );
    await f.Db.SaveChangesAsync();
    f.Sources.Add(new() { ExternalId = "shared", LoadNumber = 12 });
    Assert.True((await f.Handler.Handle(new(), default)).Success);
    Assert.Equal(2, await f.Db.Dispatches.CountAsync());
    Assert.Equal("Other", old.OrderNumber);
  }

  [Fact]
  public async Task DuplicateSourceKeysRejectWholeBatch()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    f.Sources.Add(new() { ExternalId = "duplicate", LoadNumber = 1 });
    f.Sources.Add(new() { ExternalId = "duplicate", LoadNumber = 2 });
    Assert.Equal(422, (await f.Handler.Handle(new(), default)).StatusCode);
    Assert.False(await f.Db.Dispatches.AnyAsync());
    Assert.False(await f.Db.DispatchNumberCounters.AnyAsync());
  }

  [Fact]
  public async Task DisabledImportNeedsNoAdapterAndMakesNoWrites()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var handler = new SyncDispatchesCommandHandler(
      f.Db,
      [],
      Options.Create(new DispatchImportOptions()),
      f.Reads,
      f.Memory,
      TestCache.Preparation()
    );
    Assert.Equal(409, (await handler.Handle(new(), default)).StatusCode);
    Assert.False(await f.Db.Dispatches.AnyAsync());
  }
}
