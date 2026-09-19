using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Addresses;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Addresses")]
[Trait("Kind", "Integration")]
public sealed class StopAddressServiceTests
{
  [Fact]
  public async Task ConcurrentImportWinsOverAnInFlightCorrection()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "11402 East Point Drive",
    };
    stop.SourceAddressJson = StopAddress.From(stop).Serialize();
    db.Dispatches.Add(new Dispatch { Id = Guid.NewGuid(), Stops = [stop] });
    await db.SaveChangesAsync();
    using var cache = TestCache.Create();
    var geocoder = new Geocoder
    {
      BeforeResolve = () =>
      {
        using var importing = new AppDbContext(options);
        var changed = importing.DispatchStops.Single();
        changed.Address = "11404 East Point Drive";
        changed.SourceAddressJson = StopAddress.From(changed).Serialize();
        importing.SaveChanges();
      },
    };
    await new StopAddressService(
      db,
      geocoder,
      cache,
      TestCache.Preparation()
    ).VerifyAsync([stop], default);
    db.ChangeTracker.Clear();
    var saved = await db.DispatchStops.SingleAsync();
    Assert.Equal("11404 East Point Drive", saved.Address);
    Assert.Null(saved.AddressVerifiedAt);
  }

  [Fact]
  public async Task PersistsCorrectionPreservesSourceAndDoesNotRepeatLookup()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "11402 East Point Drive",
      City = "Laredo",
      Province = "TX",
      Country = "USA",
      ZipCode = "78045",
    };
    db.Dispatches.Add(new Dispatch { Id = Guid.NewGuid(), Stops = [stop] });
    await db.SaveChangesAsync();
    using var cache = TestCache.Create();
    var geocoder = new Geocoder();
    var service = new StopAddressService(
      db,
      geocoder,
      cache,
      TestCache.Preparation()
    );
    await service.VerifyAsync([stop], default);
    db.ChangeTracker.Clear();
    var persisted = await db.DispatchStops.SingleAsync();
    Assert.Equal("11402 EastPoint Dr", persisted.Address);
    Assert.Contains("11402 East Point Drive", persisted.SourceAddressJson);
    Assert.NotNull(persisted.AddressVerifiedAt);
    Assert.Equal(27.62m, persisted.Latitude);
    Assert.Equal(-99.47m, persisted.Longitude);
    Assert.Equal(
      persisted.Address,
      (await db.Dispatches.Select(DispatchProjection.Details).SingleAsync())
        .Stops.Single()
        .Address
    );
    await service.VerifyAsync([persisted], default);
    Assert.Equal(1, geocoder.Calls);

    var source = new ExternalDispatchStop
    {
      Address = "11402 East Point Drive",
      City = "Laredo",
      Province = "TX",
      Country = "USA",
      ZipCode = "78045",
      Latitude = 27.5m,
      Longitude = -99.5m,
    };
    DispatchMapper.UpdateStop(persisted, source, null, null, null, null);
    Assert.Equal("11402 EastPoint Dr", persisted.Address);
    Assert.NotNull(persisted.AddressVerifiedAt);
    Assert.Equal(27.62m, persisted.Latitude);
    Assert.Equal(-99.47m, persisted.Longitude);
    source.Address = "11404 East Point Drive";
    DispatchMapper.UpdateStop(persisted, source, null, null, null, null);
    Assert.Equal(source.Address, persisted.Address);
    Assert.Null(persisted.AddressVerifiedAt);
    Assert.Null(persisted.AddressRetryAfter);
    Assert.Equal(source.Latitude, persisted.Latitude);
    Assert.Equal(source.Longitude, persisted.Longitude);
  }

  [Fact]
  public async Task ExpirationRestoresSourceAndFailuresKeepItWithBoundedRetries()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var source = new StopAddress(
      "100 Original Rd",
      "Town",
      "TX",
      "USA",
      "78045"
    );
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "100 Corrected Rd",
      SourceAddressJson = source.Serialize(),
      AddressVerifiedAt = DateTime.UtcNow.AddDays(-30),
    };
    db.Dispatches.Add(new Dispatch { Id = Guid.NewGuid(), Stops = [stop] });
    await db.SaveChangesAsync();
    using var cache = TestCache.Create();
    var geocoder = new Geocoder { Fail = true };
    var service = new StopAddressService(
      db,
      geocoder,
      cache,
      TestCache.Preparation()
    );
    await service.ExpireAsync(default);
    Assert.Equal(source.Address, stop.Address);
    Assert.Null(stop.AddressVerifiedAt);
    await service.VerifyAsync([stop], default);
    await service.VerifyAsync([stop], default);
    Assert.Equal(1, geocoder.Calls);
    Assert.Equal(source.Address, stop.Address);
    Assert.Null(stop.AddressVerifiedAt);
    Assert.True(stop.AddressRetryAfter > DateTime.UtcNow);
    Assert.True(stop.AddressRetryAfter < DateTime.UtcNow.AddHours(25));
  }

  [Fact]
  public async Task VerificationPropagatesProvenanceToTheDetachedPreparationInput()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "11402 East Point Drive",
      City = "Laredo",
      Province = "TX",
    };
    db.Dispatches.Add(new Dispatch { Id = Guid.NewGuid(), Stops = [stop] });
    await db.SaveChangesAsync();
    var detached = await db.DispatchStops.AsNoTracking().SingleAsync();
    using var cache = TestCache.Create();
    var geocoder = new Geocoder();
    await new StopAddressService(
      db,
      geocoder,
      cache,
      TestCache.Preparation()
    ).VerifyAsync([detached], default);
    Assert.NotNull(detached.AddressVerifiedAt);
    Assert.NotEmpty(detached.SourceAddressJson);
    Assert.Null(detached.AddressRetryAfter);
    Assert.Equal(
      new RoutePoint(27.62, -99.47),
      StopLocation.VerifiedPoint(detached, DateTime.UtcNow)
    );
    await new StopAddressService(
      db,
      geocoder,
      cache,
      TestCache.Preparation()
    ).VerifyAsync([detached], default);
    Assert.Equal(1, geocoder.Calls);
  }

  [Fact]
  public async Task MalformedExpiredSourceCannotBlockOtherRowsOrBecomeTrustedCoordinates()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var valid = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "Corrected",
      AddressVerifiedAt = DateTime.UtcNow.AddDays(-30),
      SourceAddressJson = new StopAddress(
        "Original",
        "Town",
        "TX",
        "US",
        "78045"
      ).Serialize(),
    };
    var invalid = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "Preserved",
      AddressVerifiedAt = DateTime.UtcNow.AddDays(-30),
      SourceAddressJson = "broken-source",
      Latitude = 40,
      Longitude = -80,
    };
    db.Dispatches.Add(
      new Dispatch { Id = Guid.NewGuid(), Stops = [valid, invalid] }
    );
    await db.SaveChangesAsync();
    using var cache = TestCache.Create();
    var geocoder = new Geocoder();
    var service = new StopAddressService(
      db,
      geocoder,
      cache,
      TestCache.Preparation()
    );
    await service.ExpireAsync(default);
    Assert.Equal("Original", valid.Address);
    Assert.Null(valid.AddressVerifiedAt);
    Assert.Equal("Preserved", invalid.Address);
    Assert.Equal("broken-source", invalid.SourceAddressJson);
    Assert.Null(invalid.AddressVerifiedAt);
    Assert.True(invalid.AddressRetryAfter > DateTime.UtcNow);
    Assert.Null(StopLocation.VerifiedPoint(invalid, DateTime.UtcNow));
    await service.VerifyAsync([invalid], default);
    Assert.Equal(0, geocoder.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CommittedPredecessorAddressChangesImmediatelyDirtyKnownSuccessors(
    bool expire
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "address-owner" };
    db.Trucks.Add(truck);
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "11402 East Point Drive",
      AddressVerifiedAt = expire ? DateTime.UtcNow.AddDays(-30) : null,
      SourceAddressJson = new StopAddress(
        "11402 East Point Drive",
        "Laredo",
        "TX",
        "US",
        "78045"
      ).Serialize(),
    };
    db.Dispatches.Add(
      new Dispatch
      {
        Id = Guid.NewGuid(),
        TruckId = truck.Id,
        Stops = [stop],
      }
    );
    await db.SaveChangesAsync();
    using var cache = TestCache.Create();
    var queue = TestCache.Preparation();
    var successor = Guid.NewGuid();
    queue.Observe(successor, truck.Id, "ready", 1);
    queue.Complete(Assert.Single(queue.Take(1)), "ready", truck.Id);
    var service = new StopAddressService(db, new Geocoder(), cache, queue);
    if (expire)
      await service.ExpireAsync(default);
    else
      await service.VerifyAsync([stop], default);
    Assert.Contains(
      queue.Take(10),
      work => work.DispatchId == successor && work.ConnectionVersion == 1
    );
  }

  private sealed class Geocoder : IAddressGeocoder
  {
    public int Calls;
    public bool Fail;
    public Action? BeforeResolve;

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<ResolvedAddress> ResolveAsync(
      string address,
      CancellationToken ct
    )
    {
      Calls++;
      BeforeResolve?.Invoke();
      if (Fail)
        throw new RoutePlanningException("Address needs confirmation.");
      return Task.FromResult(
        new ResolvedAddress(
          new(27.62, -99.47),
          "11402 EastPoint Dr",
          "Laredo",
          "TX",
          "US",
          "78045"
        )
      );
    }
  }
}
