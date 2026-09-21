using Application;
using Application.Features.Execution.Interfaces;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fuel.Interfaces;
using Application.Features.Routing.Interfaces;
using Application.Interfaces;
using Domain.Models.Routing;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Support;

internal sealed class PlanningPipelineFixture(
  SqliteConnection connection,
  ServiceProvider root,
  AsyncServiceScope scope,
  PlanningPipelineFixture.RejectingRouter router
) : IAsyncDisposable
{
  public RejectingRouter Router => router;
  public IServiceProvider Services => scope.ServiceProvider;

  public static async Task<PlanningPipelineFixture> CreateAsync()
  {
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddApplication();
    services.AddSingleton<ICurrentCompany>(new TestCompany());
    services.AddSingleton<IFuelExchangeRateStore>(
      new MemoryFuelExchangeRateStore()
    );
    services.AddSingleton<IFuelExchangeRateProvider>(
      new StubFuelExchangeRateProvider()
    );
    services.AddSingleton<ICurrentUser>(
      new CurrentUser(new HttpContextAccessor())
    );
    services.AddDbContext<AppDbContext>(options =>
      options.UseSqlite(connection)
    );
    services.AddScoped<IAppDbContext>(provider =>
      provider.GetRequiredService<AppDbContext>()
    );
    services.AddScoped<IExecutionReadScope, ExecutionReadScope>();
    services.AddScoped<ISavedRoutePlanReader, SavedRoutePlanReader>();
    services.AddScoped<IDeadheadHistoryReader, DeadheadHistoryReader>();
    services.AddScoped<IPlanningPublicationScope, PlanningPublicationScope>();
    services.AddScoped<IPlanningRefreshStore, PlanningRefreshStore>();
    services.AddSingleton<IDriverHosProvider>(new PlanningTestServices.NoHos());
    var router = new RejectingRouter();
    services.AddSingleton<IRoutingProvider>(router);
    var root = services.BuildServiceProvider(
      new ServiceProviderOptions { ValidateScopes = true }
    );
    var scope = root.CreateAsyncScope();
    await scope
      .ServiceProvider.GetRequiredService<AppDbContext>()
      .Database.EnsureCreatedAsync();
    return new(connection, root, scope, router);
  }

  public async ValueTask DisposeAsync()
  {
    await scope.DisposeAsync();
    await root.DisposeAsync();
    await connection.DisposeAsync();
  }

  internal sealed class RejectingRouter : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      throw new InvalidOperationException("No route call expected.");
    }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Calls++;
      throw new InvalidOperationException("No geocode expected.");
    }
  }
}
