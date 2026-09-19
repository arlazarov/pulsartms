using Application.Features.Integrations.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Support;

public sealed class IntegrationCredentialFixture : IAsyncDisposable
{
  private readonly SqliteConnection connection;
  private readonly string connectionString;
  private readonly CredentialWrites writes = new();
  public ServiceProvider Services { get; private set; }
  public IIntegrationCredentialStore Store =>
    Services.GetRequiredService<IIntegrationCredentialStore>();
  public int SaveCount => writes.Count;
  public IDataProtectionProvider Protection =>
    Services.GetRequiredService<IDataProtectionProvider>();

  private IntegrationCredentialFixture(
    SqliteConnection connection,
    string connectionString
  )
  {
    this.connection = connection;
    this.connectionString = connectionString;
    Services = CreateProvider();
  }

  public static async Task<IntegrationCredentialFixture> CreateAsync()
  {
    var connectionString =
      $"Data Source=credentials-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
    var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    var fixture = new IntegrationCredentialFixture(
      connection,
      connectionString
    );
    await fixture.WithDbAsync(async db =>
    {
      await db.Database.EnsureCreatedAsync();
    });
    fixture
      .Protection.CreateProtector("credential-fixture-warmup")
      .Protect("fixture");
    return fixture;
  }

  private ServiceProvider CreateProvider()
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(TimeProvider.System);
    services.AddDbContext<AppDbContext>(options =>
      options.UseSqlite(connectionString).AddInterceptors(writes)
    );
    services
      .AddDataProtection()
      .SetApplicationName("AMFTMS")
      .PersistKeysToDbContext<AppDbContext>();
    services.AddSingleton<
      IIntegrationCredentialStore,
      IntegrationCredentialStore
    >();
    return services.BuildServiceProvider(
      new ServiceProviderOptions
      {
        ValidateScopes = true,
        ValidateOnBuild = true,
      }
    );
  }

  public async Task WithDbAsync(Func<AppDbContext, Task> action)
  {
    await using var scope = Services.CreateAsyncScope();
    await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
  }

  public void HoldConcurrentCredentialWrites() => writes.HoldTwoWrites();

  public async Task ReplaceServiceProviderAsync()
  {
    await Services.DisposeAsync();
    Services = CreateProvider();
  }

  public async ValueTask DisposeAsync()
  {
    await Services.DisposeAsync();
    await connection.DisposeAsync();
  }

  private sealed class CredentialWrites : SaveChangesInterceptor
  {
    private int count;
    private int remaining;
    private TaskCompletionSource? gate;
    public int Count => Volatile.Read(ref count);

    public void HoldTwoWrites()
    {
      remaining = 2;
      gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override InterceptionResult<int> SavingChanges(
      DbContextEventData eventData,
      InterceptionResult<int> result
    )
    {
      Interlocked.Increment(ref count);
      return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      Interlocked.Increment(ref count);
      var current = gate;
      if (
        current is not null
        && eventData
          .Context!.ChangeTracker.Entries<IntegrationCredentialSetting>()
          .Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified
          )
      )
      {
        if (Interlocked.Decrement(ref remaining) == 0)
          current.TrySetResult();
        await current.Task.WaitAsync(
          TimeSpan.FromSeconds(5),
          cancellationToken
        );
      }
      return result;
    }
  }
}
