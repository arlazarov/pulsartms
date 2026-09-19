using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class SwitchPlanningRulesTests
{
  [Fact]
  public void RepeatedSourcePickupsAreNotImplicitlyTreatedAsTransfer()
  {
    var load = Load();
    var change = Change(load);
    Assert.Null(SwitchPlanningRules.Split(load, change));
    var explicitChange = change with
    {
      ReleaseVisitId = load.Stops[1].Id,
      ReceiveVisitId = load.Stops[2].Id,
    };
    var split = SwitchPlanningRules.Split(load, explicitChange);
    Assert.NotNull(split);
    Assert.Equal(load.Stops[0].Id, Assert.Single(split.Value.Before).Id);
    Assert.Equal(load.Stops[3].Id, Assert.Single(split.Value.After).Id);
    Assert.All(load.Stops, x => Assert.False(x.IsCompleted));
  }

  [Fact]
  public void DropAndHookMayBePlannedDaysApartButNeverInReverse()
  {
    var change = Change(Load()) with
    {
      PlannedReleaseAt = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero),
      PlannedReceiveAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero),
    };
    var request = new PlanSwitchRequest(
      Guid.NewGuid(),
      "Confirmed transfer yard",
      null,
      [change]
    )
    {
      Latitude = 35,
      Longitude = -80,
    };
    Assert.True(SwitchPlanningRules.Valid(request));
    Assert.False(
      SwitchPlanningRules.Valid(
        request with
        {
          Loads =
          [
            change with
            {
              PlannedReceiveAt = change.PlannedReleaseAt.Value.AddDays(-1),
            },
          ],
        }
      )
    );
    Assert.False(
      SwitchPlanningRules.Valid(
        request with
        {
          Loads =
          [
            change with
            {
              Incoming = change.Incoming with { TrailerId = null },
            },
          ],
        }
      )
    );
  }

  private static SwitchLoadChange Change(DispatchEntity load)
  {
    var trailer = Guid.NewGuid();
    return new(
      load.Id,
      null,
      null,
      ExecutionSnapshots.Fingerprint(load),
      new(Guid.NewGuid(), Guid.NewGuid(), trailer),
      new(Guid.NewGuid(), Guid.NewGuid(), trailer)
    )
    {
      TransferKind = "drop_hook",
    };
  }

  [Fact]
  public void CompletedImportBoundaryNeedsExplicitConfirmation()
  {
    var load = Load();
    load.Stops[1].PickedUpAt = new(2026, 9, 14, 11, 24, 0);
    var change = Change(load) with
    {
      ReleaseVisitId = load.Stops[1].Id,
      ReceiveVisitId = load.Stops[2].Id,
    };
    Assert.Null(SwitchPlanningRules.Split(load, change));
    var split = SwitchPlanningRules.Split(load, change, true);
    Assert.NotNull(split);
    Assert.Equal(load.Stops[3].Id, Assert.Single(split.Value.After).Id);
    Assert.NotNull(load.Stops[1].PickedUpAt);
    Assert.Null(load.Stops[2].PickedUpAt);
  }

  [Fact]
  public void ConfirmationWithoutTimeSurvivesSnapshotCopy()
  {
    var stop = new DispatchStop { ExecutionCompleted = true };
    var copy = ExecutionSnapshots.Copy(stop);
    Assert.True(copy.IsCompleted);
    Assert.Null(copy.ManualCompletedAt);
    Assert.Null(copy.PickedUpAt);
    copy.AwaitingHandoff = true;
    Assert.False(copy.IsCompleted);
  }

  private static DispatchEntity Load() =>
    new()
    {
      Id = Guid.NewGuid(),
      Status = "in_transit",
      Stops = Enumerable
        .Range(0, 4)
        .Select(x => new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = x,
          Job = x == 3 ? "Drop Off" : "Pick Up",
          Address = "Explicit shared site",
        })
        .ToList(),
    };
}
