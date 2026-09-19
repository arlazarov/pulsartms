using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Mileage;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Addresses")]
[Trait("Kind", "Integration")]
public sealed class ExecutionStopAddressServiceTests
{
  [Fact]
  public async Task VerificationUsesAcceptedRevisionAndKeepsSourceUntouched()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(fixture);
    var original = ExecutionStopRows.Read(leg);
    var source = fixture.Load.Stops[0].Address;
    var geocoder = new Geocoder();
    var service = Service(fixture, geocoder);
    await service.VerifyAsync(leg.Id, default);
    Assert.Equal(2, leg.Revision);
    Assert.Equal(2, geocoder.Calls);
    Assert.All(
      leg.Stops,
      stop =>
      {
        Assert.Equal(41.1m, stop.Latitude);
        Assert.NotNull(stop.AddressVerifiedAt);
        Assert.Null(stop.AddressRetryAfter);
      }
    );
    Assert.Equal(source, fixture.Load.Stops[0].Address);
    Assert.Equal(1, await fixture.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(1, await fixture.Db.ExecutionPlanningChanges.CountAsync());
    Assert.Equal(original[0].SourceAddressJson, leg.Stops[0].SourceAddressJson);
    await service.VerifyAsync(leg.Id, default);
    Assert.Equal(2, geocoder.Calls);
    Assert.Equal(2, leg.Revision);
  }

  [Fact]
  public async Task ConcurrentAssignmentCannotReceiveLateAddressResults()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(fixture);
    var geocoder = new Geocoder
    {
      BeforeResolve = () =>
        fixture
          .Db.ExecutionLegs.Where(x => x.Id == leg.Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revision, 2)),
    };
    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
      () => Service(fixture, geocoder).VerifyAsync(leg.Id, default)
    );
    fixture.Db.ChangeTracker.Clear();
    Assert.All(
      await fixture.Db.ExecutionLegStops.ToListAsync(),
      stop => Assert.Null(stop.AddressVerifiedAt)
    );
    Assert.Empty(await fixture.Db.ExecutionLegRevisions.ToListAsync());
  }

  [Fact]
  public async Task RecordedMileagePreventsAutomaticLocationReplacement()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(fixture);
    fixture.Db.Movements.Add(
      new Movement
      {
        Id = Guid.NewGuid(),
        IdempotencyKey = Guid.NewGuid(),
        TruckId = leg.TruckId,
        ExecutionLegId = leg.Id,
        FromVisitId = leg.Stops[0].Id,
        ToVisitId = leg.Stops[1].Id,
        ActualMiles = 12,
        RecordedAt = fixture.Clock.GetUtcNow().UtcDateTime,
      }
    );
    await fixture.Db.SaveChangesAsync();
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => Service(fixture, new()).VerifyAsync(leg.Id, default)
    );
    Assert.Equal(1, leg.Revision);
    Assert.Empty(await fixture.Db.ExecutionLegRevisions.ToListAsync());
  }

  [Fact]
  public async Task FailedVerificationPersistsRetryWithoutInventingLocation()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var leg = await SeedAsync(fixture);
    var retry = fixture.Clock.GetUtcNow().AddHours(1).UtcDateTime;
    var geocoder = new Geocoder { RetryAt = retry };
    var service = Service(fixture, geocoder);
    await service.VerifyAsync(leg.Id, default);
    Assert.All(
      leg.Stops,
      stop =>
      {
        Assert.Null(stop.AddressVerifiedAt);
        Assert.Equal(retry, stop.AddressRetryAfter);
        Assert.Equal(40m, stop.Latitude);
      }
    );
    await service.VerifyAsync(leg.Id, default);
    Assert.Equal(2, geocoder.Calls);
  }

  private static ExecutionStopAddressService Service(
    StopCompletionFixture fixture,
    Geocoder geocoder
  ) => new(fixture.Db, geocoder, fixture.Reads, fixture.Queue, fixture.Clock);

  private static async Task<ExecutionLeg> SeedAsync(
    StopCompletionFixture fixture
  )
  {
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "address-truck" };
    var trip = new Trip { Id = Guid.NewGuid() };
    var stops = fixture
      .Load.Stops.Take(2)
      .Select(ExecutionSnapshots.Copy)
      .ToArray();
    foreach (var stop in stops)
    {
      stop.Address = "123 Main St";
      stop.City = "Example City";
      stop.Latitude = 40;
      stop.Longitude = -80;
      stop.AddressVerifiedAt = null;
      stop.SourceAddressJson = StopAddress.From(stop).Serialize();
    }
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      TripId = trip.Id,
      Revision = 1,
      Status = "planned",
      Stops = ExecutionStopRows.Capture(stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = fixture.Load.Id,
          StartVisitId = stops[0].Id,
          EndVisitId = stops[^1].Id,
        },
      ],
    };
    fixture.Db.Trucks.Add(truck);
    fixture.Db.Trips.Add(trip);
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();
    return leg;
  }

  private sealed class Geocoder : IAddressGeocoder
  {
    public int Calls { get; private set; }
    public Func<Task>? BeforeResolve { get; init; }
    public DateTime? RetryAt { get; init; }

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public async Task<ResolvedAddress> ResolveAsync(
      string address,
      CancellationToken ct
    )
    {
      Calls++;
      if (BeforeResolve is not null)
        await BeforeResolve();
      if (RetryAt is { } retry)
        throw new RoutePlanningException("Address unavailable.", retry);
      return new(
        new(41.1, -80.1),
        "123 Main Street",
        "Example City",
        "ON",
        "Canada",
        "A1A1A1"
      );
    }
  }
}
