using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class StopCorrectionTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CompletingThirdStopCompletesPrefixWithoutInventingTimes(
    bool native
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var first = f.Load.Stops[0];
    first.ManualCompletedAt = f.Clock.GetUtcNow().UtcDateTime.AddHours(-2);
    first.ManualCompletionRevision = 4;
    if (native)
    {
      var trip = new Trip { Id = Guid.NewGuid() };
      f.Db.Add(trip);
      foreach (
        var stops in new[]
        {
          f.Load.Stops.Take(2).ToList(),
          f.Load.Stops.Skip(2).ToList(),
        }
      )
      {
        var truck = new Truck
        {
          Id = Guid.NewGuid(),
          UnitNumber = stops[0].Sequence.ToString(),
          ExternalId = Guid.NewGuid().ToString(),
        };
        f.Db.Add(truck);
        var leg = new ExecutionLeg
        {
          Id = Guid.NewGuid(),
          TripId = trip.Id,
          TruckId = truck.Id,
          Revision = 1,
          Status = "planned",
          Stops = ExecutionStopRows.Capture(stops),
        };
        f.Db.Add(leg);
        f.Db.LoadExecutionLegs.Add(
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = f.Load.Id,
            ExecutionLegId = leg.Id,
            StartVisitId = stops[0].Id,
            EndVisitId = stops[^1].Id,
            Sequence = stops[0].Sequence,
          }
        );
      }
    }
    await f.Db.SaveChangesAsync();
    var command = await Command(f, f.Load.Stops[2].Id, "completed");
    command.Request.CompletedAt = f.Clock.GetUtcNow().AddHours(-1);
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    var rows = result.Response!.Load.Stops;
    Assert.All(rows.Take(3), x => Assert.True(x.IsCompleted));
    Assert.All(rows.Skip(3), x => Assert.False(x.IsCompleted));
    Assert.Equal(4, first.ManualCompletionRevision);
    Assert.Null(f.Load.Stops[1].ManualCompletedAt);
    Assert.Equal(
      command.Request.CompletedAt?.UtcDateTime,
      f.Load.Stops[2].ManualCompletedAt
    );
    Assert.True((await f.CorrectionHandler().Handle(command, default)).Success);
    Assert.Equal(1, await f.Db.DispatchWorkspaceRevisions.CountAsync());
    if (native)
    {
      Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
      var accepted = (await f.Db.ExecutionLegs.ToListAsync())
        .SelectMany(ExecutionStopRows.Read)
        .ToDictionary(x => x.Id);
      Assert.All(
        rows,
        x => Assert.Equal(x.IsCompleted, accepted[x.Id].IsCompleted)
      );
    }
  }

  [Theory]
  [InlineData("stop", 2, 2, false)]
  [InlineData("stop", 2, 2, true)]
  [InlineData("range", 1, 3, false)]
  [InlineData("range", 1, 3, true)]
  [InlineData("onward", 2, 4, false)]
  [InlineData("onward", 2, 4, true)]
  public async Task DriverScopeRetainsTopologyAndUnselectedDrivers(
    string scope,
    int from,
    int to,
    bool clearDriver
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "Scoped" };
    var oldDriver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "old",
      Name = "Old",
    };
    var newDriver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "new",
      Name = "New",
    };
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      DriverId = oldDriver.Id,
      Revision = 1,
      Status = "completed",
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
    };
    f.Db.AddRange(truck, oldDriver, newDriver, trip, leg);
    f.Db.LoadExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = leg.Id,
        StartVisitId = f.Load.Stops[0].Id,
        EndVisitId = f.Load.Stops[^1].Id,
      }
    );
    await f.Db.SaveChangesAsync();
    var command = await Command(f, f.Load.Stops[2].Id, "keep");
    var request = command.Request;
    request.ChangeAssignment = true;
    request.ChangeTruck =
      request.ChangeTrailer =
      request.ChangeCoDriver =
        false;
    request.ChangeDriver = true;
    request.DriverId = clearDriver ? null : newDriver.Id;
    request.AssignmentScope = scope;
    request.FromStopId = f.Load.Stops[from].Id;
    request.ToStopId = f.Load.Stops[to].Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    f.Db.ChangeTracker.Clear();
    var saved = await f.Db.ExecutionLegs.SingleAsync();
    var stops = ExecutionStopRows.Read(saved);
    Assert.Equal(5, stops.Count);
    for (var n = 0; n < stops.Count; n++)
      Assert.Equal(
        n >= from && n <= to ? request.DriverId : oldDriver.Id,
        stops[n].DriverId
      );
    Assert.Equal("completed", saved.Status);
    Assert.Equal(2, saved.Revision);
    Assert.Equal(1, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(0, await f.Db.DispatchSwitchOperations.CountAsync());
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public async Task CorrectionNeedsNoReasonAndStillRecordsActorAndChanges(
    string? reason
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var command = await Command(f, f.Load.Stops[0].Id, "completed");
    command.Request.Reason = reason!;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    var audit = await f.Db.DispatchWorkspaceRevisions.SingleAsync();
    Assert.NotEqual(Guid.Empty, audit.RecordedBy);
    Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, audit.RecordedAt);
    Assert.Contains("completed", audit.Summary);
    Assert.NotEqual("{}", audit.BeforeJson);
    Assert.NotEqual("{}", audit.SnapshotJson);
  }

  [Fact]
  public async Task ReopensProviderCompletionWithoutErasingProviderFacts()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    f.Load.Status = "completed";
    var stop = f.Load.Stops[^1];
    stop.DeliveredAt = f.Clock.GetUtcNow().UtcDateTime.AddHours(-1);
    await f.Db.SaveChangesAsync();
    var command = await Command(f, stop.Id, "pending");
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.False(stop.IsCompleted);
    Assert.NotNull(stop.DeliveredAt);
    Assert.Equal("in_transit", f.Load.Status);
    Assert.False(
      result.Response!.Load.Stops.Single(x => x.Id == stop.Id).IsCompleted
    );
    var audit = await f.Db.DispatchWorkspaceRevisions.SingleAsync();
    Assert.Contains("completed", audit.BeforeJson);
    Assert.Contains("pending", audit.Summary);
    Assert.Contains("Correction fixture", audit.Summary);
    Assert.NotEqual("{}", audit.SnapshotJson);
    Assert.True((await f.CorrectionHandler().Handle(command, default)).Success);
    Assert.Equal(1, await f.Db.DispatchWorkspaceRevisions.CountAsync());
    f.Load.Status = "completed";
    DispatchWorkspaceImport.RestoreCommercial(
      f.Load,
      await f.Db.DispatchWorkspaces.SingleAsync()
    );
    Assert.Equal("in_transit", f.Load.Status);
    Assert.False(stop.IsCompleted);
  }

  [Fact]
  public async Task CompletionWithoutTimeHasNoFabricatedActualAndCanBeCorrected()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var stop = f.Load.Stops[1];
    var command = await Command(f, stop.Id, "completed");
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success);
    Assert.True(stop.IsCompleted);
    Assert.Null(stop.ManualCompletedAt);
    Assert.True(
      result.Response!.Load.Stops.Single(x => x.Id == stop.Id).IsCompleted
    );
    var reopen = await Command(f, stop.Id, "pending");
    Assert.True((await f.CorrectionHandler().Handle(reopen, default)).Success);
    Assert.False(stop.IsCompleted);
  }

  [Fact]
  public async Task StaleEditorCannotOverwriteCorrectionAndRetryCannotChangePayload()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var first = await Command(f, f.Load.Stops[0].Id, "completed");
    var second = await Command(f, f.Load.Stops[0].Id, "pending");
    Assert.True((await f.CorrectionHandler().Handle(first, default)).Success);
    Assert.Equal(
      409,
      (await f.CorrectionHandler().Handle(second, default)).StatusCode
    );
    first.Request.Completion = "pending";
    Assert.Equal(
      409,
      (await f.CorrectionHandler().Handle(first, default)).StatusCode
    );
    Assert.True(f.Load.Stops[0].IsCompleted);
  }

  [Fact]
  public async Task AssignmentCorrectionOnCompletedLoadKeepsCompletionAndSourceBaseline()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    f.Load.Status = "completed";
    foreach (var stop in f.Load.Stops)
      stop.DeliveredAt = f.Clock.GetUtcNow().UtcDateTime.AddHours(-1);
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Corrected truck",
    };
    var trailer = new Trailer
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Corrected trailer",
    };
    var driver = new Driver { Id = Guid.NewGuid(), Name = "Corrected driver" };
    f.Db.AddRange(truck, trailer, driver);
    await f.Db.SaveChangesAsync();
    var command = await Command(f, f.Load.Stops[0].Id, "keep");
    command.Request.ChangeAssignment = true;
    command.Request.TruckId = truck.Id;
    command.Request.TrailerId = trailer.Id;
    command.Request.DriverId = driver.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.Equal("completed", f.Load.Status);
    Assert.All(
      f.Load.Stops,
      stop =>
      {
        Assert.True(stop.IsCompleted);
        Assert.Equal(truck.Id, stop.TruckId);
        Assert.Equal(trailer.Id, stop.TrailerId);
        Assert.Equal(driver.Id, stop.DriverId);
      }
    );
    var workspace = await f.Db.DispatchWorkspaces.SingleAsync();
    Assert.True(workspace.OwnsStops);
    Assert.DoesNotContain("Corrected truck", workspace.SourceStopsJson);
    Assert.Equal(truck.Id, f.Load.PlanningTruckId);
  }

  [Theory]
  [InlineData("current", false)]
  [InlineData("all", true)]
  [InlineData("onward", true)]
  [InlineData("range", true)]
  public async Task NativeLegCorrectionHonorsScopeAndKeepsCompletionLocal(
    string scope,
    bool changesSecond
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "old",
      UnitNumber = "Old",
    };
    var replacement = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "new",
      UnitNumber = "New",
    };
    var trip = new Trip { Id = Guid.NewGuid() };
    var first = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "completed",
      Revision = 1,
      Stops = ExecutionStopRows.Capture(f.Load.Stops.Take(2)),
    };
    var second = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "planned",
      Revision = 1,
      Stops = ExecutionStopRows.Capture(f.Load.Stops.Skip(2)),
    };
    f.Db.AddRange(truck, replacement, trip, first, second);
    f.Db.LoadExecutionLegs.AddRange(
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = first.Id,
        Sequence = 1,
      },
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = second.Id,
        Sequence = 2,
      }
    );
    await f.Db.SaveChangesAsync();
    var command = await Command(f, f.Load.Stops[0].Id, "completed");
    command.Request.ChangeAssignment = true;
    command.Request.AssignmentScope = scope;
    command.Request.FromStopId = f.Load.Stops[0].Id;
    command.Request.ToStopId = f.Load.Stops[^1].Id;
    command.Request.TruckId = replacement.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.Equal(replacement.Id, first.TruckId);
    Assert.Equal("completed", first.Status);
    Assert.Equal(changesSecond ? replacement.Id : truck.Id, second.TruckId);
    Assert.True(ExecutionStopRows.Read(first)[0].IsCompleted);
    Assert.False(ExecutionStopRows.Read(first)[1].IsCompleted);
    Assert.True(result.Response!.Load.Stops[0].IsCompleted);
    var versions = await f.Db.ExecutionLegRevisions.ToListAsync();
    Assert.Equal(changesSecond ? 2 : 1, versions.Count);
    Assert.All(
      versions,
      version =>
      {
        Assert.Equal(command.Request.IdempotencyKey, version.CorrelationId);
        Assert.Equal(f.Actor.Id, version.RecordedBy);
        Assert.Equal(
          replacement.Id,
          ExecutionRevisionFacts.Read(version).TruckId
        );
      }
    );
  }

  [Fact]
  public async Task TruckOnlyCorrectionPreservesDifferentTrailersAndDrivers()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "Replacement" };
    var trailers = f
      .Load.Stops.Select(
        (_, i) =>
          new Trailer
          {
            Id = Guid.NewGuid(),
            UnitNumber = $"Trailer {i}",
            ExternalId = $"trailer-{i}",
          }
      )
      .ToList();
    var drivers = f
      .Load.Stops.Select(
        (_, i) =>
          new Driver
          {
            Id = Guid.NewGuid(),
            Name = $"Driver {i}",
            ExternalId = $"driver-{i}",
          }
      )
      .ToList();
    f.Db.Add(truck);
    f.Db.AddRange(trailers);
    f.Db.AddRange(drivers);
    for (var i = 0; i < f.Load.Stops.Count; i++)
    {
      f.Load.Stops[i].TrailerId = trailers[i].Id;
      f.Load.Stops[i].DriverId = drivers[i].Id;
    }
    await f.Db.SaveChangesAsync();
    var command = await Command(f, f.Load.Stops[0].Id, "keep");
    command.Request.ChangeAssignment = true;
    command.Request.AssignmentScope = "all";
    command.Request.ChangeTruck = true;
    command.Request.ChangeTrailer = false;
    command.Request.ChangeDriver = false;
    command.Request.ChangeCoDriver = false;
    command.Request.TruckId = truck.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    for (var i = 0; i < f.Load.Stops.Count; i++)
    {
      Assert.Equal(truck.Id, f.Load.Stops[i].TruckId);
      Assert.Equal(trailers[i].Id, f.Load.Stops[i].TrailerId);
      Assert.Equal(drivers[i].Id, f.Load.Stops[i].DriverId);
    }
  }

  [Theory]
  [InlineData("onward")]
  [InlineData("range")]
  public async Task PartialAssignmentRangeIsRejectedWithoutWrites(string scope)
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var command = await Command(f, f.Load.Stops[1].Id, "completed");
    command.Request.ChangeAssignment = true;
    command.Request.AssignmentScope = scope;
    command.Request.FromStopId = f.Load.Stops[1].Id;
    command.Request.ToStopId = f.Load.Stops[^1].Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.Equal(400, result.StatusCode);
    Assert.Contains("full stop range", string.Join(";", result.Errors ?? []));
    Assert.Equal(0, await f.Db.DispatchWorkspaceRevisions.CountAsync());
    Assert.False(f.Load.Stops[1].IsCompleted);
  }

  [Fact]
  public async Task DriverChangeChecksPreservedCoDriverAtEveryStop()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      Name = "Existing co-driver",
      ExternalId = "scope-driver",
    };
    f.Db.Add(driver);
    f.Load.Stops[^1].CoDriverId = driver.Id;
    await f.Db.SaveChangesAsync();
    var command = await Command(f, f.Load.Stops[0].Id, "keep");
    command.Request.ChangeAssignment = true;
    command.Request.ChangeTruck = false;
    command.Request.ChangeTrailer = false;
    command.Request.ChangeDriver = true;
    command.Request.ChangeCoDriver = false;
    command.Request.DriverId = driver.Id;
    var result = await f.CorrectionHandler().Handle(command, default);
    Assert.Equal(400, result.StatusCode);
    Assert.Contains(
      "Driver and co-driver must differ",
      string.Join(";", result.Errors ?? [])
    );
    Assert.Equal(0, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task DeniesUnauthorizedAndInvalidResourcesWithoutAuditWrites()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var command = await Command(f, f.Load.Stops[0].Id, "completed");
    Assert.Equal(
      403,
      (await f.CorrectionHandler("User").Handle(command, default)).StatusCode
    );
    command.Request.ChangeAssignment = true;
    command.Request.TruckId = Guid.NewGuid();
    Assert.Equal(
      400,
      (await f.CorrectionHandler().Handle(command, default)).StatusCode
    );
    Assert.Equal(0, await f.Db.DispatchWorkspaceRevisions.CountAsync());
    Assert.False(f.Load.Stops[0].IsCompleted);
  }

  private static async Task<CorrectDispatchStopCommand> Command(
    StopCompletionFixture f,
    Guid stopId,
    string completion
  )
  {
    var state = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    return new(
      f.Load.Id,
      stopId,
      new()
      {
        ExpectedRevision = state!.Response.Revision,
        SourceFingerprint = state.Response.SourceFingerprint,
        IdempotencyKey = Guid.NewGuid(),
        Completion = completion,
        Reason = "Correction fixture",
      }
    );
  }
}
