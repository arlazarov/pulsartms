using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Fuel;

// Stage 4e of docs/architecture/current-work.md: what each caller of
// TruckFuelPlans.ApplyAsync costs, and which of that work the callers of
// one planning refresh repeat although their final inputs differ. The
// order is a refresh's: the summary's preparation (itinerary and hours
// supplied), the summary publisher's display copy (a fresh itinerary and
// hours), the refresh operation before its price refresh (neither
// supplied, on the prepared state), the price refresh itself, then the
// fleet loop (neither, a new state). The price refresh reads the saved
// plan from the store, not the read cache - it decides whether to
// recalculate from the latest saved plan, a justified fresh read - and
// then runs the same check. Statements are counted where they reach the database and
// labelled by the table they read first. Local SQLite timings are
// recorded for orientation only; they say nothing about production.
//
// Limitation: a statement repeated with the same text is not proved to
// have the same parameters or to read the same dependency versions; the
// repetition of the check is established by the code (the same saved
// plan, remaining roads and loads) and by FuelSavedInputsShareTests, not
// by these texts.
//
// Shared: the refresh shares the check within the operation
// (FuelSavedInputsValidation.Share, as PlanningRefreshOperation does);
// the fleet loop is another operation and checks for itself.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed partial class FuelCallerCostTests(ITestOutputHelper output)
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task EachCallerOfOneRefreshIsCountedWithTheWorkItRepeats(
    bool shared
  )
  {
    var probe = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: probe
    );
    var profile = await f.PrepareCalculationAsync();
    var root = f.State.Plan!;
    await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile)
      {
        ExecutionLegId = root.ExecutionLegId,
        AssignmentRevision = root.AssignmentRevision,
      },
      default
    );
    var work = (
      await f.Services.PlanningInputs.ReadFreshAsync(
        root.TruckId,
        default,
        includeHos: true
      )
    )!;
    var prepared = await StateAsync(f);
    var copy = await StateAsync(f);
    var later = await StateAsync(f);
    var calls = new List<Call>();
    async Task Measure(string caller, Func<Task> apply)
    {
      probe.Clear();
      var started = Stopwatch.GetTimestamp();
      await apply();
      calls.Add(
        new(
          caller,
          Stopwatch.GetElapsedTime(started).TotalMilliseconds,
          [.. probe.Statements]
        )
      );
    }

    var operation = shared ? f.Services.SavedFuelInputs.Share() : null;
    await Measure(
      "summary preparation",
      () =>
        f.Services.FuelPlans.ApplyAsync(
          prepared,
          default,
          work.Itinerary,
          work.Hos
        )
    );
    await Measure(
      "summary publisher",
      () =>
        f.Services.FuelPlans.ApplyAsync(copy, default, work.Itinerary, work.Hos)
    );
    await Measure(
      "refresh before prices",
      () => f.Services.FuelPlans.ApplyAsync(prepared, default)
    );
    var prices = new FuelPriceRefreshService(
      new TruckFuelPlanStore(f.Db, NullLogger<TruckFuelPlanStore>.Instance),
      f.Services.FuelInputs,
      f.Services.Sender,
      new CarrierFuelPrices(f.Services.Sender),
      TimeProvider.System,
      f.Services.SavedFuelInputs
    );
    await Measure(
      "price refresh",
      () =>
        prices.RefreshAsync(
          new(root.TruckId, root.DispatchId, 1, prepared, null)
          {
            ExecutionLegId = root.ExecutionLegId,
            AssignmentRevision = root.AssignmentRevision,
          },
          default
        )
    );
    operation?.Dispose();
    await Measure(
      "fleet loop",
      () => f.Services.FuelPlans.ApplyAsync(later, default)
    );

    // The sub-steps on their own, warm, to name what the callers repeat:
    // checking the saved fuel plan's roads and history against the
    // database, and capturing the truck's itinerary for display.
    var saved = (await f.Services.FuelPlans.ReadAsync(root.TruckId, default))!;
    await Measure(
      "saved inputs check alone",
      () => f.Services.SavedFuelInputs.MatchesAsync(saved, later.Plan!, default)
    );
    f.Services.Reads.InvalidateItem("planning-inputs", root.TruckId);
    await Measure(
      "display itinerary alone, cold",
      () => f.Services.FuelInputs.ReadDisplayAsync(root.TruckId, default)
    );

    var seen = new HashSet<string>();
    foreach (var call in calls)
    {
      var repeated = call.Statements.Count(seen.Contains);
      output.WriteLine(
        $"{call.Caller}: {call.Statements.Count} statements "
          + $"({repeated} repeated from earlier rows), "
          + $"{call.Milliseconds:0.0} ms local: "
          + string.Join(", ", call.Statements.Select(Label))
      );
      seen.UnionWith(call.Statements);
    }
    if (Environment.GetEnvironmentVariable("PULSARTMS_ARTIFACT_DIR") is { } dir)
      await File.WriteAllTextAsync(
        Path.Combine(dir, "fuel-callers.json"),
        JsonSerializer.Serialize(
          calls,
          new JsonSerializerOptions { WriteIndented = true }
        )
      );

    var check = calls[5].Statements;
    var itinerary = calls[6].Statements;
    Assert.All(
      new[] { prepared, copy, later },
      state => Assert.False(state.Plan!.FuelPlan!.NeedsRefresh)
    );
    // Each projection also reads what was handed over to the driver, once
    // and fresh - hand-overs are not shared within an operation. Audit D6
    // made that read run for a plan without fuel stops too (this fixture's):
    // a hand-over accepted after a publication that dropped every stop must
    // still show as withdrawn. One statement per projecting caller, none
    // for the price refresh, which does not project.
    Assert.Equal(
      [1, 1, 1, 0, 1, 0, 0],
      calls.Select(x => x.Statements.Count(Records))
    );
    Assert.Equal(
      shared ? [8, 0, 4, 1, 6, 6, 4] : [8, 6, 10, 7, 6, 6, 4],
      calls.Select(x => x.Statements.Count(sql => !Records(sql)))
    );
    if (shared)
      return;
    // Every caller repeats the same check of the same saved plan against
    // the same saved roads and history, whatever it supplied.
    Assert.All(
      calls.Take(5),
      call =>
        Assert.Equal(
          check,
          call.Statements.Where(sql => !Records(sql)).TakeLast(check.Count)
        )
    );
    // Beyond the check, the summary reads the saved plan and its geometry
    // once for every caller after it.
    Assert.Equal(
      ["TruckFuelPlans", "TruckFuelPlans"],
      calls[0].Statements.Take(2).Select(Label)
    );
    // The refresh operation supplies no itinerary and captures one; the
    // fleet loop's capture is then served from the read cache.
    Assert.Equal(itinerary, calls[2].Statements.Take(itinerary.Count));
  }

  private sealed record Call(
    string Caller,
    double Milliseconds,
    IReadOnlyList<string> Statements
  );

  private static bool Records(string sql) => sql.Contains("\"FuelVisitSends\"");

  private static string Label(string sql) =>
    From().Match(sql) is { Success: true } match
      ? match.Groups[1].Value
      : sql.Split('\n')[0];

  [GeneratedRegex("FROM \"(\\w+)\"")]
  private static partial Regex From();

  private static async Task<RoutePlanningState> StateAsync(
    SavedFuelHorizonFixture f
  )
  {
    var plan = f.State.Plan!;
    var load = await f.Services.Routes.LoadAsync(
      f.Current.Id,
      default,
      plan.ExecutionLegId
    );
    return await f.Services.Routes.GetAsync(load, default);
  }
}
