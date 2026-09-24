using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Domain.Entities.Dispatch;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

// One rule for every workspace save that carries a retry identity: the
// recorded answer comes back only for the same request, from the same
// person, on the same load; any other use of the identity is refused.
// Creating a load has no load to compare yet.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchWorkspaceReceiptTests
{
  [Fact]
  public async Task OnlyTheSameRequestBySamePersonOnTheSameLoadIsReplayed()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var load = new DispatchEntity { Id = Guid.NewGuid(), LoadNumber = 1 };
    var actor = Guid.NewGuid();
    var key = Guid.NewGuid();
    f.Db.Dispatches.Add(load);
    f.Db.DispatchWorkspaceRevisions.Add(
      new DispatchWorkspaceRevision
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        Revision = 7,
        IdempotencyKey = key,
        RequestHash = "request",
        SnapshotJson = DispatchWorkspaceData.Write(
          new DispatchWorkspaceResponse { Revision = 7 }
        ),
        RecordedAt = DateTime.UtcNow,
        RecordedBy = actor,
      }
    );
    await f.Db.SaveChangesAsync();

    Task<DispatchWorkspaceReceipts.Replay?> Find(
      Guid identity,
      string hash,
      Guid person,
      Guid? dispatch
    ) =>
      DispatchWorkspaceReceipts.FindAsync(
        f.Db,
        identity,
        hash,
        person,
        dispatch,
        default
      );

    Assert.Null(await Find(Guid.NewGuid(), "request", actor, load.Id));
    Assert.Equal(
      7,
      (await Find(key, "request", actor, load.Id))!.Answer!.Revision
    );
    Assert.Equal(
      7,
      (await Find(key, "request", actor, null))!.Answer!.Revision
    );
    Assert.Null((await Find(key, "other", actor, load.Id))!.Answer);
    Assert.Null((await Find(key, "request", Guid.NewGuid(), load.Id))!.Answer);
    Assert.Null((await Find(key, "request", actor, Guid.NewGuid()))!.Answer);
  }
}
