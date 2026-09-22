using Application.Features.Eta.Interfaces;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Interfaces;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Microsoft.Extensions.Http;

namespace FleetLoadProbe;

internal sealed class SyntheticProviders(int fleetSize)
  : IRoutingProvider,
    IRouteAlternativesProvider,
    IDriverHosProvider,
    IHosHistoryProvider
{
  public long RouteCalls;
  public long HosCalls;
  public bool IsConfigured => true;

  public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) =>
    throw new InvalidOperationException("Fixture stops must have coordinates.");

  public async Task<TruckRoute> CalculateAsync(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    Interlocked.Increment(ref RouteCalls);
    await Task.Delay(50, ct);
    var legs = points
      .Zip(
        points.Skip(1),
        (a, b) =>
        {
          var kilometers = RouteGeometry.Distance(a, b) * 1.609344;
          var count = Math.Max(2, (int)Math.Ceiling(kilometers / .05) + 1);
          var path = Enumerable
            .Range(0, count)
            .Select(i =>
            {
              var t = (double)i / (count - 1);
              var envelope = Math.Sin(Math.PI * t);
              var bend =
                Math.Sin(t * kilometers * Math.PI / 2) * .002
                + Math.Sin(t * kilometers * Math.PI / 20) * .008;
              return new RoutePoint(
                a.Latitude + (b.Latitude - a.Latitude) * t + bend * envelope,
                a.Longitude + (b.Longitude - a.Longitude) * t
              );
            })
            .ToList();
          var miles = path.Zip(path.Skip(1), RouteGeometry.Distance).Sum();
          return new RouteLeg(miles, miles / 55 * 3600, path);
        }
      )
      .ToList();
    return RouteViaGeometry.Join(legs, [], DateTime.UtcNow);
  }

  public async Task<IReadOnlyList<TruckRoute>> CalculateAlternativesAsync(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  ) => [await CalculateAsync(points, profile, ct)];

  public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
    CancellationToken ct
  ) =>
    Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
      Enumerable
        .Range(0, fleetSize)
        .ToDictionary(
          i => $"load-driver-{i}",
          _ => new DriverHosClocks
          {
            BreakMs = 8 * 3600000L,
            DriveMs = 11 * 3600000L,
            ShiftMs = 14 * 3600000L,
            CycleMs = 50 * 3600000L,
            UpdatedAt = DateTime.UtcNow,
            CurrentDutyStatus = "driving",
          }
        )
    );

  public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct)
  {
    Interlocked.Increment(ref HosCalls);
    var now = DateTimeOffset.UtcNow;
    var start = new DateTimeOffset(
      now.UtcDateTime.Date.AddDays(-8),
      TimeSpan.Zero
    );
    var periods = Enumerable
      .Range(0, 8)
      .SelectMany(day =>
        new[]
        {
          new HosPeriod(
            start.AddDays(day),
            start.AddDays(day).AddHours(14),
            "offDuty"
          ),
          new HosPeriod(
            start.AddDays(day).AddHours(14),
            start.AddDays(day).AddHours(23),
            "driving"
          ),
          new HosPeriod(
            start.AddDays(day).AddHours(23),
            start.AddDays(day + 1),
            "onDuty"
          ),
        }
      )
      .ToArray();
    return Task.FromResult<HosHistory?>(
      new(
        start,
        now,
        "America/Chicago",
        0,
        new(8, 70, 34),
        new(7, 70, 36),
        periods
      )
    );
  }
}

internal sealed class NoExternalHttp : IHttpMessageHandlerBuilderFilter
{
  public Action<HttpMessageHandlerBuilder> Configure(
    Action<HttpMessageHandlerBuilder> next
  ) =>
    builder =>
    {
      next(builder);
      builder.PrimaryHandler = new Reject();
    };

  private sealed class Reject : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    ) =>
      throw new InvalidOperationException(
        "External HTTP is forbidden in the synthetic load fixture."
      );
  }
}
