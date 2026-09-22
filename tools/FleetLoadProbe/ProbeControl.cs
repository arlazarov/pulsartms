using System.Runtime;
using System.Text.Json;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetLoadProbe;

internal sealed class ProbeControl(IServiceScopeFactory scopes)
{
  public sealed record Work(Guid Load, Guid Leg);

  public sealed record Vehicle(Guid Truck, string Unit, Work[] Work);

  public Vehicle[] Vehicles { get; private set; } = [];
  private int tick;

  public async Task LoadAsync()
  {
    await using var scope = scopes.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var owner = sp.GetRequiredService<ICurrentCompany>()
      .As(ProbeSeed.CompanyId);
    var db = sp.GetRequiredService<AppDbContext>();
    var trucks = await db.Trucks.OrderBy(x => x.UnitNumber).ToArrayAsync();
    var loads = await db
      .LoadExecutionLegs.Include(x => x.ExecutionLeg)
      .Join(
        db.Dispatches,
        x => x.DispatchId,
        d => d.Id,
        (x, d) =>
          new
          {
            x.ExecutionLeg.TruckId,
            d.LoadNumber,
            Load = d.Id,
            Leg = x.ExecutionLegId,
          }
      )
      .OrderBy(x => x.LoadNumber)
      .ToArrayAsync();
    Vehicles = trucks
      .Select(t => new Vehicle(
        t.Id,
        t.UnitNumber,
        loads
          .Where(x => x.TruckId == t.Id)
          .Select(x => new Work(x.Load, x.Leg))
          .ToArray()
      ))
      .ToArray();
    await TelemetryAsync(false);
  }

  public async Task TelemetryAsync(bool detour)
  {
    var step = Interlocked.Increment(ref tick);
    var now = DateTime.UtcNow;
    var trucks = Vehicles
      .Select(
        (v, i) =>
          new TruckLocation
          {
            TruckId = v.Truck,
            UnitNumber = v.Unit,
            TruckExternalId = $"load-truck-{i}",
            DriverName = $"Test Driver {i + 1:000}",
            TrailerNumber = $"TEST-T{i + 1:000}",
            Latitude = 40m + i * .0001m + (detour ? .08m : 0),
            Longitude = -120.7m + i * .003m + step * .001m,
            Speed = 55,
            Heading = 90,
            FuelPercent = 45,
            FuelUpdatedAt = now,
            UpdatedAt = now,
            ObservedAt = now,
            EngineState = "On",
            FormattedLocation = "Synthetic test location",
          }
      )
      .ToList();
    await using var scope = scopes.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var owner = sp.GetRequiredService<ICurrentCompany>()
      .As(ProbeSeed.CompanyId);
    sp.GetRequiredService<ServerTelemetry>().Set(new() { Trucks = trucks });
    await sp.GetRequiredService<ITruckLocationStore>()
      .WriteAsync(trucks, default);
  }

  public async Task EnqueueAsync(string version, CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var owner = sp.GetRequiredService<ICurrentCompany>()
      .As(ProbeSeed.CompanyId);
    var store = sp.GetRequiredService<IPlanningRefreshStore>();
    foreach (var v in Vehicles)
      await store.RequestAsync(
        new(v.Work[0].Load, v.Work[0].Leg, 1),
        version,
        DateTime.UtcNow,
        ct
      );
    sp.GetRequiredService<PlanningRefreshSignal>().Pulse();
  }

  public async Task<object> RefreshAsync(int index, CancellationToken ct)
  {
    var v = Vehicles[index];
    await using var scope = scopes.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var owner = sp.GetRequiredService<ICurrentCompany>()
      .As(ProbeSeed.CompanyId);
    var result = await sp.GetRequiredService<AutomaticPlanningService>()
      .ForTruckAsync(v.Truck, ct);
    if (result.State?.Plan is null || result.Message is not null)
      throw new InvalidOperationException(result.Message ?? "Missing route.");
    return new { prepared = true };
  }

  public async Task<object> PrepareAsync(int index, CancellationToken ct)
  {
    var v = Vehicles[index];
    await using var scope = scopes.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var owner = sp.GetRequiredService<ICurrentCompany>()
      .As(ProbeSeed.CompanyId);
    var routes = sp.GetRequiredService<RoutePlanningService>();
    var profile = await routes.ProfileAsync(v.Truck, ct);
    foreach (var work in v.Work)
    {
      var load = await routes.LoadAsync(work.Load, ct, work.Leg);
      await sp.GetRequiredService<BaseRouteService>()
        .EnsureAsync(load, profile, ct);
      await sp.GetRequiredService<DeadheadService>()
        .EnsureAsync(load, profile, ct);
    }
    var automatic = await sp.GetRequiredService<AutomaticPlanningService>()
      .ForTruckAsync(v.Truck, ct);
    if (automatic.State?.Plan is null || automatic.Message is not null)
      throw new InvalidOperationException(
        automatic.Message ?? "Missing route."
      );
    return await CalculateAsync(sp, v, profile, ct);
  }

  public async Task<object> RecalculateAsync(int index, CancellationToken ct)
  {
    var v = Vehicles[index];
    await using var scope = scopes.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    using var owner = sp.GetRequiredService<ICurrentCompany>()
      .As(ProbeSeed.CompanyId);
    return await CalculateAsync(
      sp,
      v,
      await sp.GetRequiredService<RoutePlanningService>()
        .ProfileAsync(v.Truck, ct),
      ct
    );
  }

  private static async Task<object> CalculateAsync(
    IServiceProvider sp,
    Vehicle v,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var fuel = await sp.GetRequiredService<FuelPlanningService>()
      .BuildAsync(
        v.Work[0].Load,
        new(profile) { ExecutionLegId = v.Work[0].Leg, AssignmentRevision = 1 },
        ct
      );
    if (fuel.Plan?.Stops.Count is not > 0)
      throw new InvalidOperationException(
        "No synthetic fuel stops: " + fuel.Access?.Message
      );
    var memory = sp.GetRequiredService<EtaMemory>();
    await sp.GetRequiredService<EtaForecastService>()
      .RefreshAsync(memory.Scope(v.Work[0].Load, v.Work[0].Leg), ct);
    var db = sp.GetRequiredService<AppDbContext>();
    var forecasts = await db.Set<DispatchEtaForecast>()
      .AsNoTracking()
      .Where(x => x.TruckId == v.Truck)
      .Select(x => x.ForecastJson)
      .ToArrayAsync(ct);
    var eta = forecasts
      .Select(x =>
        JsonSerializer.Deserialize<DispatchEta>(x, RoutingJson.Options)
      )
      .ToArray();
    if (eta.Length != 2 || eta.Any(x => x?.Stops.Count is not > 0))
      throw new InvalidOperationException(
        "Both loads must have populated ETA forecasts: "
          + string.Join("; ", eta.Select(x => x?.UnavailableReason))
      );
    return new
    {
      fuel = fuel.Status.ToString(),
      stops = fuel.Plan.Stops.Count,
      eta = eta.Length,
    };
  }
}

internal static class ProbeEndpoints
{
  public static void MapProbeControls(this WebApplication app)
  {
    var group = app.MapGroup("/probe").RequireAuthorization("Admin");
    group.MapPost(
      "/compact-gc",
      (IRuntimeMemoryReader memory) =>
      {
        var before = memory.Read();
        GCSettings.LargeObjectHeapCompactionMode =
          GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        return new { before, after = memory.Read() };
      }
    );
    group.MapGet("/fleet", (ProbeControl p) => p.Vehicles);
    group.MapPost(
      "/tick",
      (bool detour, ProbeControl p) => p.TelemetryAsync(detour)
    );
    group.MapPost(
      "/enqueue",
      (string version, ProbeControl p, CancellationToken ct) =>
        p.EnqueueAsync(version, ct)
    );
    group.MapPost(
      "/refresh/{index:int}",
      (int index, ProbeControl p, CancellationToken ct) =>
        p.RefreshAsync(index, ct)
    );
    group.MapPost(
      "/prepare/{index:int}",
      (int index, ProbeControl p, CancellationToken ct) =>
        p.PrepareAsync(index, ct)
    );
    group.MapPost(
      "/calculate/{index:int}",
      (int index, ProbeControl p, CancellationToken ct) =>
        p.RecalculateAsync(index, ct)
    );
    group.MapGet(
      "/state",
      async (
        AppDbContext db,
        SyntheticProviders provider,
        CancellationToken ct
      ) =>
        new
        {
          routes = await db.DispatchRoutePlans.CountAsync(ct),
          fuel = await db.TruckFuelPlans.CountAsync(ct),
          eta = await db.Set<DispatchEtaForecast>().CountAsync(ct),
          changes = await db.RouteGeometryChanges.CountAsync(ct),
          movements = await db.RouteMovementChunks.CountAsync(ct),
          pending = await db.PlanningRefreshRequests.CountAsync(
            x => x.RequestedVersion > x.CompletedVersion,
            ct
          ),
          retries = await db.PlanningRefreshRequests.CountAsync(
            x => x.Attempts > 1,
            ct
          ),
          routeCalls = Interlocked.Read(ref provider.RouteCalls),
          hosCalls = Interlocked.Read(ref provider.HosCalls),
        }
    );
  }
}
