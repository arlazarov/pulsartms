using Application.Features.Dispatch.Models;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Addresses;
using Domain.Entities.Dispatch;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Addresses")]
[Trait("Kind", "Unit")]
public sealed class StopLocationTests
{
  [Fact]
  public async Task StreetAddressOverridesImportedCityCoordinates()
  {
    var router = new Router();
    var point = await StopLocation.ResolveAsync(
      new DispatchStop
      {
        Address = "2045 S Foster Rd, San Antonio, TX",
        City = "SAN ANTONIO",
        Province = "TX",
        Latitude = 29.4251905m,
        Longitude = -98.4945922m,
      },
      router,
      default
    );
    Assert.Equal(router.Result, point);
    Assert.Equal("2045 S Foster Rd, San Antonio, TX", router.Address);
  }

  [Fact]
  public async Task CoordinatesRemainUsableWithoutAnAddress()
  {
    var router = new Router();
    var point = await StopLocation.ResolveAsync(
      new DispatchStop { Latitude = 40, Longitude = -80 },
      router,
      default
    );
    Assert.Equal(40, point.Latitude);
    Assert.Null(router.Address);
  }

  [Fact]
  public async Task FreshManagedVerificationReusesThePersistedStreetPointWithoutGeocoding()
  {
    var router = new Router();
    var stop = Verified();
    var point = await StopLocation.ResolveAsync(stop, router, default);
    Assert.Equal(new RoutePoint(40, -80), point);
    Assert.Null(router.Address);
    using var cancelled = new CancellationTokenSource();
    await cancelled.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => StopLocation.ResolveAsync(stop, router, cancelled.Token)
    );
  }

  [Theory]
  [InlineData("expired")]
  [InlineData("future")]
  [InlineData("missing-source")]
  [InlineData("invalid-source")]
  [InlineData("invalid-point")]
  [InlineData("retry")]
  public async Task UntrustedOrExpiredVerificationCannotBypassAddressResolution(
    string defect
  )
  {
    var stop = Verified();
    if (defect == "expired")
      stop.AddressVerifiedAt = DateTime.UtcNow.AddDays(-29);
    if (defect == "future")
      stop.AddressVerifiedAt = DateTime.UtcNow.AddMinutes(1);
    if (defect == "missing-source")
      stop.SourceAddressJson = "";
    if (defect == "invalid-source")
      stop.SourceAddressJson = "not-json";
    if (defect == "invalid-point")
      stop.Latitude = 100;
    if (defect == "retry")
      stop.AddressRetryAfter = DateTime.UtcNow.AddMinutes(5);
    var router = new Router();
    Assert.Equal(
      router.Result,
      await StopLocation.ResolveAsync(stop, router, default)
    );
    Assert.NotNull(router.Address);
  }

  private static DispatchStop Verified() =>
    new()
    {
      Address = "100 Main St",
      Latitude = 40,
      Longitude = -80,
      AddressVerifiedAt = DateTime.UtcNow.AddDays(-1),
      SourceAddressJson = new StopAddress(
        "100 Main Street",
        "Town",
        "TX",
        "US",
        "78045"
      ).Serialize(),
    };

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public string? Address { get; private set; }
    public RoutePoint Result { get; } = new(29.4, -98.3);

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Address = address;
      return Task.FromResult(Result);
    }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }
}
