using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Eta;

// The map's display read and the ETA worker describe the same load, so the
// forecast the worker publishes is the map's as current, whatever the
// stops carry (names, notes, commodity, appointment time zone). Added
// while diagnosing the 11007 map dash (docs/archive/2026-09/
// eta-11007-2026-09-25.md); it did not reproduce the dash.
public sealed partial class EtaChainInputTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task TheMapReadFindsTheForecastTheWorkerMade(bool detailed)
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    if (detailed)
    {
      await f
        .Db.Set<DispatchStop>()
        .ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.Commodity, "Paper goods")
            .SetProperty(x => x.Notes, "Check in at gate 3")
            .SetProperty(x => x.Name, "Target DC 3802")
            .SetProperty(x => x.AppointmentTimeZoneId, "America/New_York")
        );
      f.Db.ChangeTracker.Clear();
      f.Services.Reads.Invalidate("dispatch");
    }
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);

    var work = (
      await f.Services.PlanningInputs.ReadAsync(f.Truck.Id, default)
    )!;
    var segment = PlanningWorkPolicy.Candidates(work.Itinerary).First();
    var mapLoad = PlanningWorkPolicy.Resolve(work.Itinerary, segment);
    var etaLoad = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!.Loads[0];
    var diff = mapLoad
      .Stops.Zip(etaLoad.Stops)
      .Select(p =>
        (
          Map: JsonSerializer.Serialize(p.First),
          Eta: JsonSerializer.Serialize(p.Second)
        )
      )
      .Where(p => p.Map != p.Eta)
      .ToList();
    var state = await f.Services.Routes.GetAsync(
      mapLoad,
      default,
      displayOnly: true
    );

    Assert.Empty(diff);
    var cached = f.Services.Eta.GetCached(state);
    Assert.NotNull(cached);
    Assert.False(cached.RouteUpdatePending);
  }
}
