using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Application.Features.Dispatch.Queries;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

// Isolated HTTP harness: production handler/cache, synthetic SQLite data, no
// production configuration, authentication pipeline, telemetry or paid providers.
var date = new DateOnly(2026, 9, 6);
foreach (var fleetSize in new[] {100, 300, 1000})
{
  var connectionString = $"Data Source=load-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
  await using var keeper = new SqliteConnection(connectionString);
  await keeper.OpenAsync();
  var sql = new QueryCounter();
  var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).AddInterceptors(sql).Options;
  await using (var db = new AppDbContext(options))
  {
    await db.Database.EnsureCreatedAsync();
    for (var i = 0; i < fleetSize; i++)
    {
      var truck = new Truck {Id = Guid.NewGuid(), ExternalId = $"synthetic-{i}", UnitNumber = (11000+i).ToString(), IsActive = true};
      db.Trucks.Add(truck);
      for (var j = 0; j < 2; j++) db.Dispatches.Add(new Dispatch {
        Id = Guid.NewGuid(), TruckId = truck.Id, TruckNumber = truck.UnitNumber,
        LoadNumber = i * 2 + j + 1, OrderNumber = $"TEST-{i}-{j}", CustomerName = "Synthetic customer",
        Status = j == 0 ? "in_transit" : "assigned", ShipDate = date, DeliveryDate = date.AddDays(2+j),
        Stops = [new() {Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", City = "Origin", ScheduledDate = date},
          new() {Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", City = "Destination", ScheduledDate = date.AddDays(2+j)}]
      });
    }
    await db.SaveChangesAsync();
  }
  var builder = WebApplication.CreateBuilder(new WebApplicationOptions {Args = [], EnvironmentName = "LoadProbe"});
  builder.Logging.ClearProviders();
  builder.WebHost.UseUrls("http://127.0.0.1:0");
  await using var app = builder.Build();
  using var cache = new ReadCache(Options.Create(new SynchronizationOptions {ReadCacheSeconds = 600}));
  app.MapGet("/board", async (int page, string? search) => {
    await using var db = new AppDbContext(options);
    var response = await new GetDispatchBoardHandler(db, cache).Handle(new(Page: page, Search: search, Date: date, IncludeHos: false), default);
    return Results.Json(response);
  });
  await app.StartAsync();
  using var client = new HttpClient {BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(60)};
  await client.GetStringAsync("/board?page=1"); // JIT and EF warmup, outside samples
  foreach (var concurrency in new[] {5, 20, 50, 100})
  {
    foreach (var cold in new[] {true, false})
    {
      if (cold) cache.Invalidate("board");
      var samples = new ConcurrentBag<double>();
      var errors = 0; long bytes = 0;
      var beforeSql = sql.Count;
      var allocated = GC.GetTotalAllocatedBytes();
      var cpu = Process.GetCurrentProcess().TotalProcessorTime;
      var watch = Stopwatch.StartNew();
      var sustained = args.Contains("--sustained") && !cold && concurrency == 20 && fleetSize == 1000;
      var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var workers = Enumerable.Range(0, concurrency).Select(async worker => {
        await start.Task;
        for (var i = 0; i < (cold ? 1 : 10) || sustained && watch.Elapsed.TotalSeconds < 30 && Environment.WorkingSet < 2_000_000_000; i++)
        {
          var timer = Stopwatch.StartNew();
          try {
            var url = i % 4 == 3 ? "/board?page=1&search=110" : $"/board?page={1+(worker+i)%Math.Max(1, fleetSize/12)}";
            using var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsByteArrayAsync();
            if (!response.IsSuccessStatusCode) Interlocked.Increment(ref errors);
            Interlocked.Add(ref bytes, body.Length);
          } catch { Interlocked.Increment(ref errors); }
          samples.Add(timer.Elapsed.TotalMilliseconds);
        }
      }).ToArray();
      start.SetResult(); await Task.WhenAll(workers); watch.Stop();
      var sorted = samples.Order().ToArray();
      double P(double p) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length*p)-1, 0, sorted.Length-1)];
      Console.WriteLine(JsonSerializer.Serialize(new {fleetSize, concurrency, mode = cold ? "cold-wave" : sustained ? "warm-30s" : "warm-closed-loop", requests = sorted.Length,
        errors, elapsedMs = watch.Elapsed.TotalMilliseconds, rps = sorted.Length/watch.Elapsed.TotalSeconds,
        p50Ms = P(.5), p95Ms = P(.95), p99Ms = P(.99), maxMs = sorted[^1],
        sqlQueries = sql.Count-beforeSql, responseBytes = bytes,
        allocatedBytes = GC.GetTotalAllocatedBytes()-allocated,
        combinedServerAndClientCpuMs = (Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalMilliseconds,
        managedHeapBytes = GC.GetTotalMemory(false), workingSetBytes = Environment.WorkingSet}));
    }
  }
  await app.StopAsync();
  Console.WriteLine(JsonSerializer.Serialize(new {fleetSize, mode = "after-forced-gc", retainedManagedBytes = GC.GetTotalMemory(true), workingSetBytes = Environment.WorkingSet}));
}

sealed class QueryCounter : DbCommandInterceptor
{
  public long Count;
  public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
    InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
  { Interlocked.Increment(ref Count); return ValueTask.FromResult(result); }
}
