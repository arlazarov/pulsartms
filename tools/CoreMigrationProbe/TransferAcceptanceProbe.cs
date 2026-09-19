using Application.Caching;
using Application.Features.Execution.Commands;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace CoreMigrationProbe;

internal static class TransferAcceptanceProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    var actor = await db.Users.SingleAsync();
    var caller = new Caller(actor.IdentityUserId);
    var roles = new UserRoleService(db);
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var queue = new RoutePreparationQueue(
      Options.Create(new RoutePreparationOptions()),
      TimeProvider.System
    );
    foreach (
      var (cancel, plannedSource) in new[]
      {
        (true, false),
        (false, false),
        (false, true),
      }
    )
    {
      db.ChangeTracker.Clear();
      var trucks = Enumerable
        .Range(0, 2)
        .Select(_ => new Truck
        {
          Id = Guid.NewGuid(),
          ExternalId = Guid.NewGuid().ToString(),
          UnitNumber = Guid.NewGuid().ToString(),
          IsActive = true,
        })
        .ToArray();
      var source = new Load
      {
        Id = Guid.NewGuid(),
        Status = plannedSource ? "assigned" : "in_transit",
        TruckId = trucks[0].Id,
        LoadNumber = await db.Dispatches.MaxAsync(x => x.LoadNumber) + 1,
        Stops = Enumerable
          .Range(0, 2)
          .Select(index => new DispatchStop
          {
            Id = Guid.NewGuid(),
            Sequence = index + 1,
            Job = index == 0 ? "Pick Up" : "Drop Off",
            StateAfter = index == 0 ? "Loaded" : "Empty",
            TruckId = trucks[0].Id,
            Latitude = 40 + index,
            Longitude = -80,
          })
          .ToList(),
      };
      db.Trucks.AddRange(trucks);
      db.Dispatches.Add(source);
      await db.SaveChangesAsync();
      var command = new PlanSwitchCommand(
        new(
          Guid.NewGuid(),
          "Fixture transfer yard",
          null,
          [
            new(
              source.Id,
              null,
              null,
              ExecutionSnapshots.Fingerprint(source),
              new(trucks[0].Id, null, null),
              new(trucks[1].Id, null, null)
            )
            {
              SplitAfterVisitId = source.Stops[0].Id,
            },
          ]
        )
        {
          Latitude = 40.5m,
          Longitude = -80,
        }
      );
      var planner = new PlanSwitchHandler(
        db,
        caller,
        roles,
        TimeProvider.System,
        reads,
        queue,
        NullLogger<PlanSwitchHandler>.Instance
      );
      var planned = await planner.Handle(command, default);
      Require(planned.Success, "Transfer acceptance must commit.");
      db.ChangeTracker.Clear();
      Require(
        (await planner.Handle(command, default)).Success,
        "Transfer planning replay must survive tracker replacement."
      );
      var operation = planned.Response!;
      var participant = operation.Legs.Single();
      var ids = new[] { participant.OutgoingLegId, participant.IncomingLegId };
      Require(
        await db.ExecutionLegRevisions.CountAsync(x =>
          ids.Contains(x.ExecutionLegId) && x.Revision == 1
        ) == 2,
        "Each newly accepted transfer leg must start with one recorded revision."
      );
      if (plannedSource)
      {
        Require(
          await db.ExecutionLegs.CountAsync(x =>
            ids.Contains(x.Id) && x.Status == "planned" && x.StartedAt == null
          ) == 2,
          "Planning a source transfer must not invent actual work."
        );
        var fresh = await db
          .Dispatches.Include(x => x.Stops)
          .SingleAsync(x => x.Id == source.Id);
        fresh.Stops.OrderBy(x => x.Sequence).First().PickedUpAt =
          DateTime.UtcNow.AddMinutes(-1);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await ExecutionSourceReconciliation.ApplyAsync(
          db,
          [fresh],
          DateTime.UtcNow,
          default
        );
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        db.ChangeTracker.Clear();
        Require(
          await db.ExecutionLegs.AnyAsync(x =>
            x.Id == participant.OutgoingLegId && x.Status == "active"
          )
            && await db.ExecutionLegs.AnyAsync(x =>
              x.Id == participant.IncomingLegId && x.Status == "planned"
            ),
          "Source progress must activate only work preceding the handoff."
        );
      }
      if (cancel)
      {
        var handler = new CancelSwitchHandler(
          db,
          caller,
          roles,
          TimeProvider.System,
          reads,
          queue,
          NullLogger<CancelSwitchHandler>.Instance
        );
        var cancellation = new CancelSwitchCommand(
          operation.Id,
          new(Guid.NewGuid(), operation.Revision)
        );
        Require(
          (await handler.Handle(cancellation, default)).Success,
          "Transfer cancellation must commit through shared acceptance."
        );
        db.ChangeTracker.Clear();
        Require(
          (await handler.Handle(cancellation, default)).Success,
          "Transfer cancellation replay must not duplicate history."
        );
        Require(
          await db.LoadExecutionLegs.CountAsync(x => x.DispatchId == source.Id)
            == 1,
          "Cancellation must retain only the restored load link."
        );
      }
      else
      {
        var release = new ReleaseSwitchParticipantHandler(
          db,
          caller,
          roles,
          TimeProvider.System,
          reads,
          queue,
          NullLogger<ReleaseSwitchParticipantHandler>.Instance
        );
        var action = new ReleaseSwitchParticipantCommand(
          new(
            Guid.NewGuid(),
            operation.Id,
            participant.ParticipantId,
            1,
            1,
            plannedSource ? 2 : 1,
            null
          )
        );
        Require(
          (await release.Handle(action, default)).Success,
          "Transfer release must commit through shared acceptance."
        );
        db.ChangeTracker.Clear();
        Require(
          (await release.Handle(action, default)).Success,
          "Transfer release replay must retain the existing receipt."
        );
        var receive = new ReceiveSwitchParticipantHandler(
          db,
          caller,
          roles,
          TimeProvider.System,
          reads,
          queue,
          NullLogger<ReceiveSwitchParticipantHandler>.Instance
        );
        var receipt = new ReceiveSwitchParticipantCommand(
          new(
            Guid.NewGuid(),
            operation.Id,
            participant.ParticipantId,
            2,
            2,
            1,
            null
          )
        );
        Require(
          (await receive.Handle(receipt, default)).Success,
          "Transfer receipt must commit through shared acceptance."
        );
        db.ChangeTracker.Clear();
        Require(
          (await receive.Handle(receipt, default)).Success,
          "Transfer receipt replay must retain the existing receipt."
        );
        var final = await db.SwitchParticipants.SingleAsync(x =>
          x.Id == participant.ParticipantId
        );
        Require(
          final.ReleasedBy == actor.Id
            && final.ReceivedBy == actor.Id
            && final.ReleasedAt is null
            && final.ReceivedAt is null,
          "Unknown actual times must remain unknown after confirmation."
        );
      }
      Require(
        await db.ExecutionLegRevisions.CountAsync(x =>
          ids.Contains(x.ExecutionLegId)
        ) == (plannedSource ? 5 : 4)
          && await db.ExecutionPlanningChanges.CountAsync(x =>
            ids.Contains(x.ExecutionLegId)
          ) == (plannedSource ? 5 : 4),
        "Each accepted revision must have one history record and planning request."
      );
    }
    Console.WriteLine(
      "Transfer acceptance: planning, cancellation, independent confirmations, "
        + "unknown actual times and persisted idempotent replay passed."
    );
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }

  private sealed class Caller(string identity) : ICurrentUser
  {
    public string? IdentityUserId => identity;
    public bool IsAuthenticated => true;
  }
}
