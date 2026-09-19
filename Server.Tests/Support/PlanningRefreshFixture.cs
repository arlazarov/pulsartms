using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Support;

internal sealed class PlanningRefreshFixture : IAsyncDisposable
{
  private readonly string path = Path.Combine(
    Path.GetTempPath(),
    $"pulsr-planning-{Guid.NewGuid():N}.db"
  );
  private ServiceProvider root = null!;
  private AsyncServiceScope scope;
  public ManualTimeProvider Time { get; } = new();
  public IServiceProvider Services => scope.ServiceProvider;
  public AppDbContext Db => Services.GetRequiredService<AppDbContext>();
  public PlanningRefreshQueue Queue =>
    Services.GetRequiredService<PlanningRefreshQueue>();
  public IPlanningRefreshStore Store =>
    Services.GetRequiredService<IPlanningRefreshStore>();
  public DateTime Now => Time.GetUtcNow().UtcDateTime;

  public AsyncServiceScope NewScope() => root.CreateAsyncScope();

  public static async Task<PlanningRefreshFixture> CreateAsync(
    Action<IServiceCollection>? configure = null
  )
  {
    var fixture = new PlanningRefreshFixture();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMemoryCache();
    services.AddOptions<SynchronizationOptions>();
    services.AddSingleton<TimeProvider>(fixture.Time);
    services.AddDbContext<AppDbContext>(options =>
      options.UseSqlite($"Data Source={fixture.path};Pooling=False")
    );
    services.AddScoped<IAppDbContext>(provider =>
      provider.GetRequiredService<AppDbContext>()
    );
    services.AddScoped<IPlanningRefreshStore, PlanningRefreshStore>();
    services.AddScoped<PlanningRefreshQueue>();
    services.AddSingleton<PlanningRefreshSignal>();
    services.AddSingleton<PlanningRefreshOperation>();
    configure?.Invoke(services);
    fixture.root = services.BuildServiceProvider(
      new ServiceProviderOptions { ValidateScopes = true }
    );
    fixture.scope = fixture.root.CreateAsyncScope();
    await fixture.Db.Database.EnsureCreatedAsync();
    return fixture;
  }

  public async ValueTask DisposeAsync()
  {
    await scope.DisposeAsync();
    await root.DisposeAsync();
    File.Delete(path);
  }
}
