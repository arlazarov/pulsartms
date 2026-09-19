using Application.Caching;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CoreMigrationProbe;

internal static class ExecutionImportProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "import-execution-fixture",
      UnitNumber = "import-execution-fixture",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var source = new ExternalDispatch
    {
      ExternalId = "import-execution-fixture",
      LoadNumber = 1042,
      Status = "assigned",
      TruckNumber = truck.UnitNumber,
      Stops = new[] { "Pick Up", "Delivery" }
        .Select(
          (job, i) =>
            new ExternalDispatchStop
            {
              Sequence = i + 1,
              Job = job,
              TruckNumber = truck.UnitNumber,
              Name = "Fixture facility",
              Address = $"{i + 1} Fixture Street",
              City = "Toronto",
              Province = "ON",
              Country = "CA",
            }
        )
        .ToList(),
    };
    var provider = new Provider(source);
    var sync = new SyncDispatchesCommandHandler(
      db,
      [provider],
      Options.Create(new DispatchImportOptions { Provider = provider.Key }),
      reads,
      memory,
      new RoutePreparationQueue(
        Options.Create(new RoutePreparationOptions()),
        TimeProvider.System
      )
    );
    await Synchronize();
    var leg = await db
      .ExecutionLegs.Include(x => x.Trip)
      .SingleAsync(x => x.TruckId == truck.Id);
    Require(
      leg.Status == "planned"
        && leg.RecordedBy is null
        && leg.Trip.RecordedBy is null
        && leg.StartedAt is null,
      "System import must retain nullable provenance and unknown start."
    );
    var revision = await db.ExecutionLegRevisions.SingleAsync(x =>
      x.ExecutionLegId == leg.Id
    );
    Require(
      revision.RecordedBy is null
        && revision.Operation == "source-assignment-accepted",
      "System acceptance must persist immutable system provenance."
    );
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-2);
    await Synchronize();
    Require(
      leg.Status == "active",
      "Actual pickup must activate ordinary work."
    );
    var beforeReview = leg.Revision;
    source.DriverName = "Unresolved replacement";
    source.Stops[1].DeliveredAt = DateTime.UtcNow.AddHours(-1);
    await Synchronize();
    Require(
      leg.Status == "active"
        && leg.Revision == beforeReview
        && leg.SourceReviewReason is not null
        && leg.Stops.All(x => x.DeliveredAt is null),
      "Changed source assignment must not close accepted work."
    );
    var sourceLink = await db.DispatchSourceLinks.SingleAsync(x =>
      x.DispatchId == leg.Loads.Single().DispatchId
    );
    var proposal = DispatchWorkspaceData.Read<DispatchAssignmentProposal>(
      sourceLink.AssignmentProposalJson
    );
    Require(
      proposal.Header.Driver == source.DriverName
        && proposal.Header.DriverId is null,
      "Unresolved imported resources must remain available for comparison."
    );
    var expected = leg.Revision;
    db.ChangeTracker.Clear();
    memory.Compact(1);
    await Synchronize();
    leg = await db.ExecutionLegs.SingleAsync(x => x.TruckId == truck.Id);
    Require(
      leg.Revision == expected
        && await db.ExecutionLegRevisions.CountAsync(x =>
          x.ExecutionLegId == leg.Id
        ) == expected,
      "Imported acceptance and review replay must not duplicate history."
    );
    source.DriverName = "";
    await Synchronize();
    Require(
      leg.Status == "completed"
        && leg.CompletedAt == source.Stops[1].DeliveredAt
        && leg.SourceReviewReason is null
        && leg.StartedAt is null,
      "Restored source resources may complete the same accepted execution."
    );
    Require(
      await db.ExecutionPlanningChanges.CountAsync(x =>
        x.ExecutionLegId == leg.Id
      )
        == leg.Revision + 1,
      "Accepted changes and the separate source review must each request planning."
    );
    source.ExternalId = "invalid-completed-import";
    source.LoadNumber++;
    source.Status = "completed";
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddMinutes(-10);
    await Synchronize();
    var itinerary = await new TruckItineraryReader(
      db,
      new ExecutionReadScope(db)
    ).ReadAsync(truck.Id, DateTimeOffset.UtcNow, default);
    Require(
      itinerary is not null
        && itinerary.Segments.Any(x =>
          x.Problems.Contains(WorkReadProblem.SourceReviewRequired)
        ),
      "Unaccepted source completion must remain in the reviewable itinerary."
    );
    Console.WriteLine(
      "Imported execution bootstrap, progress, resource protection and restart replay passed."
    );

    async Task Synchronize()
    {
      var result = await sync.Handle(new(), default);
      Require(result.Success, "Fixture source synchronization must succeed.");
    }
  }

  private sealed class Provider(ExternalDispatch source) : IDispatchProvider
  {
    public string Key => "execution-fixture";
    public string DisplayName => "Execution fixture";

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<ExternalDispatch>>([source]);

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      DateOnly from,
      DateOnly to,
      CancellationToken ct = default
    ) => GetDispatchesAsync(ct);
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
