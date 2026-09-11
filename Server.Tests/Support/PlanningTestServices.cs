using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Caching;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Interfaces;
using Infrastructure.Eta;
using MediatR;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

internal sealed class PlanningTestServices : IDisposable
{
  private readonly bool ownsReads;
  public ReadCache Reads { get; }
  public RouteDisplayCache Displays { get; }
  public PlanningSettingsService Settings { get; }
  public RoutePlanningService Routes { get; }
  public DeadheadService Deadheads { get; }
  public GetDispatchBoardHandler Board { get; }
  public EtaService Eta { get; }
  public EtaForecastService Forecasts { get; }
  public EtaChainInputsService EtaInputs { get; }
  public EtaMemory EtaMemory { get; }
  public FuelPlanMemory FuelMemory { get; }
  public TruckFuelPlans FuelPlans { get; }
  public FuelScheduleEvaluator FuelSchedules { get; }
  public FuelPlanningService Fuel { get; }
  public ISender Sender { get; }

  public PlanningTestServices(IAppDbContext db, IRoutingProvider? router = null, ISender? sender = null, ReadCache? reads = null)
  {
    ownsReads = reads is null;
    Reads = reads ?? new(Options.Create(new SynchronizationOptions()));
    Displays = new(Reads);
    Settings = new(db, Reads);
    router ??= new NoRouter();
    sender ??= new BoardSender(() => Board!);
    Sender = sender;
    var profiles = new TruckPlanningProfileService(db, Reads, Settings);
    Routes = new(db, router, sender!, profiles, new(db, Reads, profiles), new(db, Options.Create(new RouteRecalculationBudgetOptions())), Reads, Options.Create(new FuelRegionOptions()),
      Options.Create(new SynchronizationOptions()), Displays, new(db, router));
    Deadheads = new(db, router, Routes, new(db), new Infrastructure.Persistence.DeadheadHistoryReader((Infrastructure.Persistence.AppDbContext)db));
    var hos = new NoHos();
    EtaMemory = new();
    Eta = new(db, hos, new RouteRegionLookup(), EtaMemory, hos, Options.Create(new EtaPlanningOptions()));
    FuelMemory = new();
    FuelPlans = new(new Infrastructure.Persistence.TruckFuelPlanStore((Infrastructure.Persistence.AppDbContext)db),
      Reads, FuelMemory, sender, Options.Create(new FuelRegionOptions()));
    FuelSchedules = new(db, hos, hos, Eta);
    Fuel = new(Routes, sender,
      new(sender, Options.Create(new FuelRegionOptions()), Routes, Deadheads),
      new(Routes, db, sender, Deadheads), Options.Create(new FuelRegionOptions()),
      FuelPlans, FuelSchedules, db);
    EtaInputs = new(db, sender, Routes, new Infrastructure.Persistence.EtaRootRouteReader((Infrastructure.Persistence.AppDbContext)db),
      new Infrastructure.Persistence.NextLoadRouteReader((Infrastructure.Persistence.AppDbContext)db),
      new Infrastructure.Persistence.DeadheadHistoryReader((Infrastructure.Persistence.AppDbContext)db), EtaMemory,
      new RouteRegionLookup(), Options.Create(new EtaPlanningOptions()));
    Forecasts = new(EtaInputs, new Infrastructure.Persistence.EtaForecastStore((Infrastructure.Persistence.AppDbContext)db), EtaMemory, Eta, Routes);
    Board = new(db, Reads, hos, Deadheads, Forecasts);
  }

  public void Dispose() { FuelMemory.Dispose(); EtaMemory.Dispose(); Displays.Dispose(); if (ownsReads) Reads.Dispose(); }

  private sealed class BoardSender(Func<GetDispatchBoardHandler> board) : ISender
  {
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) =>
      request is GetDispatchBoardQuery query ? (TResponse)(object)await board().Handle(query, ct) : throw new NotSupportedException();
    public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
  }

  internal sealed class NoHos : IDriverHosProvider, IHosHistoryProvider
  {
    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(CancellationToken ct)
      => Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(new Dictionary<string, DriverHosClocks>());
    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct) => Task.FromResult<HosHistory?>(null);
  }

  private sealed class NoRouter : IRoutingProvider
  {
    public bool IsConfigured => false;
    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) => throw new NotSupportedException();
    public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, TruckRouteProfile profile, CancellationToken ct) => throw new NotSupportedException();
  }
}
