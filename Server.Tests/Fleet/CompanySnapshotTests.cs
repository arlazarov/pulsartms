using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Domain.Models.Fleet;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class CompanySnapshotTests
{
  [Fact]
  public async Task StreamNeverReusesAnotherCompanysMatchingExternalId()
  {
    var companies = new TestCompany();
    using var stream = new FleetLocationStream(TimeProvider.System, companies);
    var provider = new StubFleetTelemetryProvider
    {
      Read = (request, _) =>
        Task.FromResult(
          new VehicleLocationStream
          {
            Data =
            [
              new()
              {
                ExternalId = "same-id",
                UpdatedAt = request.To,
                Latitude = 35,
                Longitude = -90,
              },
            ],
          }
        ),
    };
    FleetTruckInfo[] fleet =
    [
      new()
      {
        TruckId = Guid.NewGuid(),
        TruckExternalId = "same-id",
        IsActive = true,
      },
    ];
    Assert.Single(await stream.GetAsync(provider, fleet));
    provider.Read = (_, _) => Task.FromResult(new VehicleLocationStream());
    using (companies.As(Guid.NewGuid()))
      Assert.Empty(await stream.GetAsync(provider, fleet));
  }

  [Fact]
  public async Task DirectAndPublishedTelemetryArePartitionedByCompany()
  {
    var companies = new TestCompany();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new FleetTelemetryCache(memory, companies);
    var published = new ServerTelemetry(companies);
    var first = new FleetLocationsResponse
    {
      Trucks = [new() { TruckId = Guid.NewGuid() }],
    };
    var second = new FleetLocationsResponse
    {
      Trucks = [new() { TruckId = Guid.NewGuid() }],
    };
    await cache.GetAsync(_ => Task.FromResult(first), default);
    published.Set(first);
    using (companies.As(Guid.NewGuid()))
    {
      Assert.Null(cache.Latest);
      Assert.Null(published.Current);
      Assert.Same(
        second,
        await cache.GetAsync(_ => Task.FromResult(second), default)
      );
      published.Set(second);
      Assert.Same(second, published.Current);
    }
    Assert.Same(first, cache.Latest);
    Assert.Same(first, published.Current);
  }

  [Fact]
  public async Task HosClocksAndRefreshCooldownBelongToOneCompany()
  {
    var companies = new TestCompany();
    var snapshot = new DriverHosSnapshot(TimeProvider.System, companies);
    Assert.True(snapshot.TryBeginRefresh(true));
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        ["same-provider-id"] = new()
        {
          DriveMs = 100,
          UpdatedAt = DateTime.UtcNow,
        },
      }
    );
    using (companies.As(Guid.NewGuid()))
    {
      Assert.Empty(await snapshot.GetClocksAsync(default));
      Assert.True(snapshot.TryBeginRefresh(true));
      snapshot.Complete(
        new Dictionary<string, DriverHosClocks>
        {
          ["same-provider-id"] = new()
          {
            DriveMs = 200,
            UpdatedAt = DateTime.UtcNow,
          },
        }
      );
    }
    Assert.Equal(
      100,
      (await snapshot.GetClocksAsync(default))["same-provider-id"].DriveMs
    );
    Assert.False(snapshot.TryBeginRefresh(true));
  }
}
