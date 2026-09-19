using Application.Caching;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Options;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class TruckWeatherTests
{
  [Fact]
  public async Task ViewersShareTruckWeatherWithoutProviderGpsReads()
  {
    var clock = new ManualTimeProvider();
    var a = Truck(clock);
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var provider = new Weather(clock);
    var handler = new GetTruckWeatherHandler(
      new DispatchTelemetrySender(new() { Trucks = [a] }),
      provider,
      cache,
      clock
    );
    var results = await Task.WhenAll(
      handler.Handle(new(a.TruckId), default),
      handler.Handle(new(a.TruckId), default)
    );
    Assert.All(results, x => Assert.Equal(20, x.Response!.Celsius));
    Assert.Equal(1, provider.Calls);
  }

  [Fact]
  public async Task MissingOrStaleGpsDoesNotInventWeather()
  {
    var clock = new ManualTimeProvider();
    var truck = Truck(clock);
    truck.UpdatedAt = clock.GetUtcNow().AddHours(-2).UtcDateTime;
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var provider = new Weather(clock);
    var handler = new GetTruckWeatherHandler(
      new DispatchTelemetrySender(new() { Trucks = [truck] }),
      provider,
      cache,
      clock
    );
    Assert.Null((await handler.Handle(new(truck.TruckId), default)).Response);
    Assert.Null((await handler.Handle(new(Guid.NewGuid()), default)).Response);
    Assert.Equal(0, provider.Calls);
  }

  private static TruckLocation Truck(TimeProvider clock) =>
    new()
    {
      TruckId = Guid.NewGuid(),
      Latitude = 35.5m,
      Longitude = -99,
      UpdatedAt = clock.GetUtcNow().UtcDateTime,
      Speed = 60,
    };

  private sealed class Weather(TimeProvider clock) : IWeatherProvider
  {
    public int Calls { get; private set; }

    public Task<WeatherReading?> GetCurrentAsync(
      decimal latitude,
      decimal longitude,
      CancellationToken ct
    )
    {
      Calls++;
      return Task.FromResult<WeatherReading?>(
        new(20, "CLEAR", "Clear", true, clock.GetUtcNow())
      );
    }
  }
}
