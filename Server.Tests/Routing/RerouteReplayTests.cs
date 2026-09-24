using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

// GPS replayed against RerouteDecision the way production feeds it: a fix
// every ten seconds, published in a batch at every telemetry poll (sixty
// seconds), each judged once at the next planning pass (thirty seconds).
// The road runs east for a hundred miles; a fix is placed by the miles it
// has covered along it and the miles it stands north of it. A reroute here
// keeps the old road, as a provider that sends the truck back would, so
// every later reroute is one more verdict. These count verdicts on a
// replay clock; provider calls, cooldown under the real owner and failures
// are AutomaticPlanningTests' concern, and none of this is a measured
// production latency.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RerouteReplayTests
{
  private const double MilesPerDegreeLatitude = 69.05;
  private const double MilesPerDegreeLongitude = 52.99;
  private const int Seconds = 540;

  private static readonly RerouteDecision.Policy Policy = new(
    2,
    TimeSpan.FromSeconds(60),
    TimeSpan.FromSeconds(150)
  );

  [Fact]
  public void JitterAndSingleFixOutliersNeverReroute()
  {
    Assert.Empty(Replay(t => (t / 60.0, Jitter(t, .15))));
    Assert.Empty(Replay(t => (t / 60.0, t is 120 or 300 ? 6 : 0)));
  }

  // The truck turns off at one minute and drives straight away from the
  // road. The old rule looked only at the newest fix at each pass and
  // waited 180 seconds from there: 300 seconds after the turn. Timed from
  // the first fix past two miles, it is 180.
  [Fact]
  public void ADepartureIsTimedFromTheFirstFixThatShowedIt()
  {
    var reroutes = Replay(t => (t / 60.0, t <= 60 ? 0 : (t - 60) / 60.0));

    Assert.Equal(240, reroutes[0]);
  }

  // A road about two miles from the planned one: fixes either side of the
  // threshold kept resetting the old rule. Back on the road only inside
  // 1.2 miles, the departure holds.
  [Fact]
  public void DrivingAlongTheThresholdIsNotResetByEveryFix()
  {
    var reroutes = Replay(t => (t / 60.0, t < 60 ? 0 : 2.1 + Jitter(t, .25)));

    Assert.Equal(120, reroutes[0]);
  }

  // Positions that stop arriving confirm nothing: fixes off the road, then
  // the same newest fix at every pass.
  [Fact]
  public void AFixSeenAgainIsNotAnotherObservation()
  {
    var reroutes = Replay(t => (t / 60.0, t < 40 ? 0 : 3), publishedUntil: 60);

    Assert.Empty(reroutes);
  }

  // Fixes too old to describe the truck neither start nor confirm a
  // departure, however far off the road they are.
  [Fact]
  public void StaleFixesConfirmNothing()
  {
    var reroutes = Replay(t => (t / 60.0, 3), age: TimeSpan.FromMinutes(20));

    Assert.Empty(reroutes);
  }

  // A truck that stays off a road the provider keeps sending it back to
  // costs at most one call per cooldown.
  [Fact]
  public void TheCooldownBoundsTheCallsForATruckThatStaysOff()
  {
    var reroutes = Replay(t => (t / 60.0, t < 30 ? 0 : 3));

    Assert.Equal([120, 270, 420], reroutes);
  }

  // After three silent minutes the first two fixes are already five miles
  // off: a departure that clear waits for no persistence time.
  [Fact]
  public void AClearDepartureNeedsTwoFixesAndNoWait()
  {
    var reroutes = Replay(t => (t / 60.0, t < 60 ? 0 : 5), silent: (70, 230));

    Assert.Equal(240, reroutes[0]);
  }

  // A truck just over the threshold and one far outlier: two fixes off
  // the road, but only one twice the threshold away, and ten seconds
  // apart. That is not a clear departure, and it has not lasted; the next
  // fixes are back on the road.
  [Fact]
  public void AModerateDepartureAndOneFarOutlierAreNotAClearDeparture()
  {
    var reroutes = Replay(t =>
      (
        t / 60.0,
        t switch
        {
          110 => 2.1,
          120 => 5,
          _ => 0,
        }
      )
    );

    Assert.Empty(reroutes);
  }

  // The seconds at which the truck was given a new road.
  private static List<int> Replay(
    Func<int, (double Along, double North)> path,
    int? publishedUntil = null,
    TimeSpan? age = null,
    (int From, int To)? silent = null
  )
  {
    // A fixed clock: freshness is judged at each pass's time, never at the
    // machine's.
    var start = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    var truckId = Guid.NewGuid();
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "", 2, At(100, 0))
    {
      Job = "Drop Off",
    };
    var plan = new RoutePlan
    {
      TruckId = truckId,
      FromCurrentPosition = true,
      Stops = [stop],
      Route = Road(),
    };
    var load = RouteWorkProjection.Capture(
      new DispatchEntity
      {
        Id = Guid.NewGuid(),
        TruckId = truckId,
        Status = "in_transit",
        Stops =
        [
          new()
          {
            Id = stop.Id,
            Sequence = 2,
            Job = "Drop Off",
            Latitude = 40,
            Longitude = (decimal)(-80 + 100 / MilesPerDegreeLongitude),
          },
        ],
      }
    );
    var geometry = new RouteGeometry(plan.Route);
    var fixes = Enumerable
      .Range(0, Seconds / 10 + 1)
      .Select(i => i * 10)
      .Where(t => silent is not { } gap || t < gap.From || t > gap.To)
      .Select(t =>
      {
        var (along, north) = path(t);
        var point = At(along, north);
        return new TruckLocation
        {
          TruckId = truckId,
          Latitude = (decimal)point.Latitude,
          Longitude = (decimal)point.Longitude,
          Speed = 60,
          UpdatedAt = start.AddSeconds(t) - (age ?? default),
          EngineState = "on",
        };
      })
      .ToList();
    var reroutes = new List<int>();
    DateTime? judged = null;
    for (var pass = 30; pass <= Seconds; pass += 30)
    {
      var now = start.AddSeconds(pass);
      var published = start.AddSeconds(
        Math.Min(pass / 60 * 60, publishedUntil ?? int.MaxValue)
      );
      var visible = fixes.Where(x => x.UpdatedAt <= published).ToList();
      if (visible.LastOrDefault() is not { } truck)
        continue;
      var fresh = visible
        .Where(x => judged is null || x.UpdatedAt > judged)
        .ToList();
      judged = truck.UpdatedAt;
      var verdict = RerouteDecision.Judge(
        plan,
        load,
        truck,
        fresh,
        Policy,
        now,
        false,
        geometry
      );
      if (!verdict.Reroute)
        continue;
      reroutes.Add(pass);
      plan.LastReroutedAt = now;
      plan.LastReroutePosition = verdict.Progress.Position;
      plan.Tracking.ClearDeviation();
    }
    return reroutes;
  }

  private static TruckRoute Road() =>
    new()
    {
      Miles = 100,
      Seconds = 6000,
      Legs =
      [
        new(
          100,
          6000,
          [.. Enumerable.Range(0, 201).Select(i => At(i / 2.0, 0))]
        ),
      ],
    };

  private static RoutePoint At(double along, double north) =>
    new(
      40 + north / MilesPerDegreeLatitude,
      -80 + along / MilesPerDegreeLongitude
    );

  // Deterministic lateral noise within [-size, size].
  private static double Jitter(int t, double size) =>
    size * Math.Sin(t * 12.9898) * Math.Cos(t * 78.233);
}
