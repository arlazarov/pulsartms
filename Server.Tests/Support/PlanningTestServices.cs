using Application.Caching;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

internal sealed class PlanningTestServices : IDisposable
{
  private readonly bool ownsReads;
  private readonly IPlanningRefreshStore refreshStore;
  private readonly PlanningRefreshSignal refreshSignal = new();

  public PlanningRefreshQueue Refreshes(
    IMemoryCache cache,
    IOptions<SynchronizationOptions> options
  ) => new(refreshStore, refreshSignal, cache, options, TimeProvider.System);

  public ReadCache Reads { get; }
  public RouteDisplayCache Displays { get; }
  public PlanningSettingsService Settings { get; }
  public FuelExchangeRateService ExchangeRates { get; }
  public RoutePlanningService Routes { get; }
  public TruckItineraryReader Itineraries { get; }
  public TruckPlanningInputsReader PlanningInputs { get; }
  public PlanningWorkPublication Publication { get; }
  public TruckPlanningProfileService Profiles { get; }
  public BaseRouteService BaseRoutes { get; }
  public FuelWorkInputsReader FuelInputs { get; }
  public SavedRoadValidation Roads { get; }
  public FuelSavedInputsValidation SavedFuelInputs { get; }
  public DeadheadService Deadheads { get; }
  public DeadheadHistoryService DeadheadHistory { get; }
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
  public NoHos Hos { get; } = new();

  public PlanningTestServices(
    IAppDbContext db,
    IRoutingProvider? router = null,
    ISender? sender = null,
    ReadCache? reads = null,
    RouteRecalculationBudgetOptions? recalculationBudget = null,
    IPlanningPublicationScope? publicationScope = null,
    IFuelExchangeRateStore? exchangeRateStore = null
  )
  {
    refreshStore = new PlanningRefreshStore((AppDbContext)db);
    ownsReads = reads is null;
    Reads = reads ?? new(Options.Create(new SynchronizationOptions()));
    Displays = new(Reads);
    Settings = new(db, Reads);
    ExchangeRates = new(
      exchangeRateStore ?? new MemoryFuelExchangeRateStore(),
      new StubFuelExchangeRateProvider(),
      Reads,
      TimeProvider.System
    );
    router ??= new NoRouter();
    sender = new BoardSender(db, () => Board!, sender);
    Sender = sender;
    var profiles = Profiles = new TruckPlanningProfileService(
      db,
      Reads,
      Settings,
      ExchangeRates
    );
    Itineraries = new(db, new ExecutionReadScope((AppDbContext)db));
    PlanningInputs = new(
      db,
      Itineraries,
      new ExecutionReadScope((AppDbContext)db),
      Reads,
      Hos,
      new SavedRoutePlanReader((AppDbContext)db),
      profiles
    );
    DeadheadHistory = new(
      db,
      new DeadheadHistoryReader((AppDbContext)db),
      new ExecutionReadScope((AppDbContext)db)
    );
    Publication = new(
      Itineraries,
      publicationScope ?? new PlanningPublicationScope((AppDbContext)db),
      DeadheadHistory
    );
    BaseRoutes = new(
      db,
      router,
      Publication,
      profiles,
      publicationScope ?? new PlanningPublicationScope((AppDbContext)db)
    );
    var routeStore = new RoutePlanStore(db, Reads, profiles);
    Routes = new(
      db,
      router,
      sender!,
      profiles,
      routeStore,
      new(
        db,
        Options.Create(
          recalculationBudget ?? new RouteRecalculationBudgetOptions()
        )
      ),
      Reads,
      Options.Create(new FuelRegionOptions()),
      Options.Create(new SynchronizationOptions()),
      Displays,
      BaseRoutes,
      PlanningInputs,
      Publication
    );
    Deadheads = new(
      db,
      router,
      Routes,
      new(db),
      DeadheadHistory,
      new(
        DeadheadHistory,
        publicationScope ?? new PlanningPublicationScope((AppDbContext)db)
      )
    );
    var hos = Hos;
    EtaMemory = new();
    Eta = new(
      db,
      hos,
      new RouteRegionLookup(),
      EtaMemory,
      hos,
      Options.Create(new EtaPlanningOptions())
    );
    FuelInputs = new(
      Itineraries,
      new ExecutionReadScope((AppDbContext)db),
      PlanningInputs
    );
    FuelMemory = new();
    Roads = new(
      db,
      new NextLoadRouteReader((AppDbContext)db),
      new SavedRoutePlanReader((AppDbContext)db),
      new ExecutionReadScope((AppDbContext)db)
    );
    SavedFuelInputs = new(
      Roads,
      DeadheadHistory,
      new ExecutionReadScope((AppDbContext)db)
    );
    FuelPlans = new(
      new TruckFuelPlanStore((AppDbContext)db),
      Reads,
      FuelMemory,
      FuelInputs,
      sender,
      Options.Create(new FuelRegionOptions()),
      SavedFuelInputs
    );
    FuelSchedules = new(db, hos, hos, Eta);
    Fuel = new(
      Routes,
      FuelInputs,
      sender,
      new(
        FuelInputs,
        Options.Create(new FuelRegionOptions()),
        Deadheads,
        new RouteRegionLookup()
      ),
      new(FuelInputs, db, Deadheads),
      Options.Create(new FuelRegionOptions()),
      FuelPlans,
      FuelSchedules,
      new RouteRegionLookup(),
      Publication,
      profiles,
      routeStore,
      Roads
    );
    EtaInputs = new(
      db,
      Itineraries,
      new ExecutionReadScope((AppDbContext)db),
      profiles,
      new SavedRoutePlanReader((AppDbContext)db),
      new NextLoadRouteReader((AppDbContext)db),
      DeadheadHistory,
      EtaMemory,
      new RouteRegionLookup(),
      Options.Create(new EtaPlanningOptions())
    );
    Forecasts = new(
      EtaInputs,
      new EtaForecastStore((AppDbContext)db),
      EtaMemory,
      Eta,
      Routes,
      Publication
    );
    Board = new(db, Reads, hos, Deadheads, Forecasts);
  }

  public void Dispose()
  {
    refreshSignal.Dispose();
    FuelMemory.Dispose();
    EtaMemory.Dispose();
    Displays.Dispose();
    if (ownsReads)
      Reads.Dispose();
  }

  private sealed class BoardSender(
    IAppDbContext db,
    Func<GetDispatchBoardHandler> board,
    ISender? supplied
  ) : ISender
  {
    public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      object? response = request switch
      {
        GetExecutionItineraryQuery query =>
          await new GetExecutionItineraryHandler(db).Handle(query, ct),
        GetTruckExecutionLoadsQuery query =>
          await new GetTruckExecutionLoadsHandler(db).Handle(query, ct),
        _ when supplied is not null => await supplied.Send(request, ct),
        GetDispatchBoardQuery query => await board().Handle(query, ct),
        _ => throw new NotSupportedException(),
      };
      return (TResponse)response!;
    }

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }

  internal sealed class NoHos : IDriverHosProvider, IHosHistoryProvider
  {
    public int ClockCalls { get; private set; }
    public int HistoryCalls { get; private set; }

    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
      CancellationToken ct
    )
    {
      ClockCalls++;
      return Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
        new Dictionary<string, DriverHosClocks>()
      );
    }

    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct)
    {
      HistoryCalls++;
      return Task.FromResult<HosHistory?>(null);
    }
  }

  private sealed class NoRouter : IRoutingProvider
  {
    public bool IsConfigured => false;

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }
}
