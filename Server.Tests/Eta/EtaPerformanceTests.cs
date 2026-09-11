using System.Diagnostics;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Infrastructure.Eta;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using Server.Tests.Support;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Allocation")]
public sealed class EtaPerformanceTests(ITestOutputHelper output)
{
  private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
  private const int Iterations = 10;

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void WarmCalculationReusesRegionsAndAllocationsDoNotGrowWithGeometry(bool withHistory)
  {
    var sparse = Measure(2, withHistory);
    var dense = Measure(10_001, withHistory);
    WriteMeasurement("Sparse synthetic single-country lookup (2 points)", sparse);
    WriteMeasurement("Dense synthetic single-country lookup (10,001 points)", dense);

    Assert.Null(sparse.Result.UnavailableReason);
    Assert.Null(dense.Result.UnavailableReason);
    Assert.Single(sparse.Result.Stops);
    Assert.Single(dense.Result.Stops);
    if (withHistory)
      Assert.Contains(dense.Result.Assumptions, assumption => assumption.StartsWith("Verified HOS history:", StringComparison.Ordinal));
    Assert.Equal(0, dense.WarmGeometryLookups);
    Assert.InRange(dense.WarmLookups, 0, Iterations * 2);
    Assert.True(dense.WarmBytesPerCalculation <= sparse.WarmBytesPerCalculation + 4096,
      $"Dense geometry allocated {dense.WarmBytesPerCalculation:N0} B per warm calculation; sparse geometry allocated {sparse.WarmBytesPerCalculation:N0} B.");
  }

  [Fact]
  public void WarmCalculationReusesRealLocalTimezoneLookups()
  {
    var measured = Measure(1001, false, realRegions: true);
    WriteMeasurement("Real local GeoTimeZone lookup (1,001 points, no network)", measured);
    Assert.Null(measured.Result.UnavailableReason);
    Assert.Single(measured.Result.Stops);
    Assert.Equal(0, measured.WarmGeometryLookups);
    Assert.InRange(measured.WarmLookups, 0, Iterations * 2);
  }

  [Fact]
  public void CycleAlternativeReplaysReuseCompiledDenseGeometry()
  {
    var sparse = Measure(2, true, cycleShortage: true);
    var dense = Measure(10_001, true, cycleShortage: true);
    WriteMeasurement("Cycle alternatives sparse", sparse);
    WriteMeasurement("Cycle alternatives dense", dense);
    Assert.NotNull(Assert.Single(dense.Result.Stops).Hours!.FirstCycleShortageAt);
    Assert.Contains(dense.Result.Stops[0].Hours!.Alternatives, alternative => alternative.Kind == "recap");
    Assert.Equal(0, dense.WarmGeometryLookups);
    Assert.InRange(dense.WarmLookups, Iterations * 4, Iterations * 6);
    Assert.True(dense.WarmBytesPerCalculation <= sparse.WarmBytesPerCalculation + 4096);
  }

  private Measurement Measure(int pointCount, bool withHistory, bool realRegions = false, bool cycleShortage = false)
  {
    var points = Enumerable.Range(0, pointCount)
      .Select(i => new RoutePoint(35, -81 + (realRegions ? 1d : 10d) * i / (pointCount - 1))).ToList();
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(), DispatchId = Guid.NewGuid(), TruckId = Guid.NewGuid(), Version = 1,
      FromCurrentPosition = true,
      Stops = [new(Guid.NewGuid(), "Delivery", "", 1, points[^1])],
      Route = new() { Miles = 600, Seconds = 36_000, Legs = [new(600, 36_000, points)] }
    };
    var state = new RoutePlanningState(new(), plan,
      new(0, 600, 36_000, 0, false, false, Start.UtcDateTime, points[0]), 50, Start.UtcDateTime, true);
    var regions = new CountingRegions(points[0], points[^1], realRegions ? new RouteRegionLookup() : null);
    var service = new EtaService(null!, null!, regions, new EtaMemory(), new PlanningTestServices.NoHos(),
      Options.Create(new EtaPlanningOptions()));
    var clocks = new DriverHosClocks
    {
      DriveMs = 11 * 3_600_000L, ShiftMs = 14 * 3_600_000L, CycleMs = 70 * 3_600_000L,
      BreakMs = 8 * 3_600_000L, UpdatedAt = Start.UtcDateTime
    };
    var history = withHistory ? CreateHistory() : null;
    if (withHistory)
    {
      clocks.DriveMs = (long)(3.25 * 3_600_000);
      clocks.ShiftMs = 6 * 3_600_000L;
      clocks.CycleMs = 6 * 3_600_000L;
      clocks.BreakMs = (long)(.25 * 3_600_000);
      clocks.CurrentDutyStatus = "driving";
    }
    if (cycleShortage)
    {
      history = HosForecastFixture.History(Start);
      clocks = HosForecastFixture.Clocks(Start, history, drive: 10);
      plan.Stops[0] = plan.Stops[0] with { ScheduledDate = new(2026, 9, 12), ScheduledTime = new(12, 0) };
    }

    var before = GC.GetAllocatedBytesForCurrentThread();
    var started = Stopwatch.GetTimestamp();
    var result = service.Calculate(state, clocks, Start.UtcDateTime, history);
    var coldMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    var coldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    var coldLookups = regions.Lookups;
    for (var i = 0; i < 3; i++) GC.KeepAlive(service.Calculate(state, clocks, Start.UtcDateTime, history));
    regions.Reset();
    before = GC.GetAllocatedBytesForCurrentThread();
    started = Stopwatch.GetTimestamp();
    for (var i = 0; i < Iterations; i++)
    {
      result = service.Calculate(state, clocks, Start.UtcDateTime, history);
      GC.KeepAlive(result);
    }
    var warmMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds / Iterations;
    var warmBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Iterations;
    return new(result, coldBytes, coldMilliseconds, coldLookups, warmBytes, warmMilliseconds,
      regions.Lookups, regions.GeometryLookups);
  }

  private void WriteMeasurement(string name, Measurement value) => output.WriteLine(
    $"{name}: cold {value.ColdBytes:N0} B, {value.ColdMilliseconds:F3} ms, {value.ColdLookups:N0} region lookups; " +
    $"warm {value.WarmBytesPerCalculation:N0} B/calculation, {value.WarmMillisecondsPerCalculation:F3} ms/calculation, " +
    $"{value.WarmLookups:N0} region lookups ({value.WarmGeometryLookups:N0} geometry lookups) across {Iterations} calculations.");

  private static HosHistory CreateHistory()
  {
    var from = Start.AddDays(-8);
    var periods = new List<HosPeriod>();
    for (var day = 0; day < 8; day++)
    {
      var begin = from.AddDays(day);
      periods.Add(new(begin, begin.AddHours(16), "offDuty"));
      periods.Add(new(begin.AddHours(16), begin.AddHours(16.25), "onDuty"));
      periods.Add(new(begin.AddHours(16.25), begin.AddDays(1), "driving"));
    }
    return new(from, Start, "Etc/UTC", 0, new(8, 70, 34), null, periods);
  }

  private sealed class CountingRegions(RoutePoint initial, RoutePoint destination, IRouteRegionLookup? local) : IRouteRegionLookup
  {
    private static readonly RouteRegion Region = new("US", "Etc/UTC", false);
    public int Lookups { get; private set; }
    public int GeometryLookups { get; private set; }
    public RouteRegion Find(RoutePoint point)
    {
      Lookups++;
      if (point != initial && point != destination) GeometryLookups++;
      return local?.Find(point) ?? Region;
    }
    public void Reset() => (Lookups, GeometryLookups) = (0, 0);
  }

  private sealed record Measurement(DispatchEta Result, long ColdBytes, double ColdMilliseconds,
    int ColdLookups, long WarmBytesPerCalculation, double WarmMillisecondsPerCalculation,
    int WarmLookups, int WarmGeometryLookups);
}
