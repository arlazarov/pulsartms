using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Application;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class ReadCycleProbe
{
  public static async Task RunAsync()
  {
    var config = new ConfigurationBuilder()
      .SetBasePath(Path.GetFullPath("Server/API"))
      .AddJsonFile("appsettings.json")
      .AddUserSecrets("pulsartms-api-local")
      .Build();
    await using var connection = new NpgsqlConnection(
      config.GetConnectionString("DefaultConnection")
    );
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(
      IsolationLevel.RepeatableRead
    );
    await using (
      var command = new NpgsqlCommand(
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",
        connection,
        transaction
      )
    )
      await command.ExecuteNonQueryAsync();
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(config);
    services.AddLogging();
    services.AddApplication();
    services.AddInfrastructure(config);
    services.Configure<SynchronizationOptions>(
      config.GetSection("Synchronization")
    );
    services.AddScoped(_ =>
    {
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseNpgsql(connection)
          .Options
      );
      db.Database.UseTransaction(transaction);
      return db;
    });
    await using var provider = services.BuildServiceProvider();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    for (var iteration = 0; iteration < 4; iteration++)
    {
      await using var scope = provider.CreateAsyncScope();
      var reader =
        scope.ServiceProvider.GetRequiredService<RoutePreviewService>();
      var before = GC.GetTotalAllocatedBytes(precise: true);
      var timer = Stopwatch.StartNew();
      var results = await reader.GetAsync(timeout.Token);
      timer.Stop();
      var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            stage = "fleet-preview",
            iteration,
            routes = results.Count,
            allocatedBytes = allocated,
            milliseconds = timer.Elapsed.TotalMilliseconds,
          }
        )
      );
      GC.KeepAlive(results);
    }
    await transaction.RollbackAsync();
  }
}
