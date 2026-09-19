using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Options;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Infrastructure;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class PerformanceReadProbe
{
  public static async Task RunAsync(bool payloadsOnly = false)
  {
    var config = new ConfigurationBuilder()
      .SetBasePath(Path.GetFullPath("Server/API"))
      .AddJsonFile("appsettings.json")
      .AddJsonFile("appsettings.Development.json")
      .AddUserSecrets("pulsartms-api-local")
      .AddEnvironmentVariables()
      .Build();
    await using var connection = new NpgsqlConnection(
      config.GetConnectionString("DefaultConnection")
    );
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using (
      var settings = new NpgsqlCommand(
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='10s'",
        connection,
        transaction
      )
    )
      await settings.ExecuteNonQueryAsync();
    using var sql = new Commands();
    using var subscription = DiagnosticListener.AllListeners.Subscribe(sql);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var ct = timeout.Token;
    await using var provider = Build();
    if (payloadsOnly)
    {
      await ReadPayloadsAsync(provider, ct);
      await transaction.RollbackAsync();
      return;
    }
    var boardQuery = new GetDispatchBoardQuery(
      Date: DateOnly.FromDateTime(DateTime.Now)
    );
    var board = await Measure("board_cold_full", () => Send(boardQuery));
    await Measure("board_warm_full_1", () => Send(boardQuery));
    await Measure("board_warm_full_2", () => Send(boardQuery));
    await Measure(
      "board_warm_no_eta",
      () => Send(boardQuery with { IncludeEta = false })
    );
    await Measure(
      "board_warm_no_financials",
      () => Send(boardQuery with { IncludeFinancials = false })
    );
    await Measure(
      "board_warm_core",
      () =>
        Send(
          boardQuery with
          {
            IncludeHos = false,
            IncludeEta = false,
            IncludeFinancials = false,
          }
        )
    );
    await Measure(
      "board_compact_financials",
      () =>
        Send(
          new GetDispatchBoardEnrichmentQuery(
            boardQuery with
            {
              IncludeFinancials = true,
            }
          )
        )
    );
    await Measure(
      "board_compact_eta",
      () =>
        Send(
          new GetDispatchBoardEnrichmentQuery(
            boardQuery with
            {
              IncludeFinancials = false,
            }
          )
        )
    );
    await Measure(
      "board_warm_identities",
      () => Send(boardQuery with { IdentitiesOnly = true })
    );
    await using (var coldCore = Build())
      await Measure(
        "board_core_cold_application_caches",
        async () =>
        {
          await using var scope = coldCore.CreateAsyncScope();
          return await scope
            .ServiceProvider.GetRequiredService<ISender>()
            .Send(
              boardQuery with
              {
                IncludeHos = false,
                IncludeEta = false,
                IncludeFinancials = false,
              },
              ct
            );
        }
      );
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          kind = "board_size",
          trucks = board.Response?.Items.Count,
          loads = board.Response?.Items.Sum(row => row.Dispatches.Count),
        }
      )
    );

    var truckId = board
      .Response?.Items.FirstOrDefault(row => row.TruckNumber == "11007")
      ?.TruckId;
    if (truckId is { } truck)
    {
      await using var previews = Build();
      for (var sample = 0; sample < 3; sample++)
      {
        var preview = await Measure(
          $"truck_preview_{(sample == 0 ? "cold" : "warm_" + sample)}",
          async () =>
          {
            await using var scope = previews.CreateAsyncScope();
            return await scope
              .ServiceProvider.GetRequiredService<RoutePreviewService>()
              .ForTruckAsync(truck, ct);
          }
        );
        Console.WriteLine(
          JsonSerializer.Serialize(
            new
            {
              kind = "preview_size",
              sample,
              hasPlan = preview.State?.Plan is not null,
              displayPoints = preview.State?.Plan?.Route.Legs.Sum(leg =>
                leg.Points.Count
              ),
            }
          )
        );
      }
      var fleet = await Measure(
        "fleet_previews_after_single",
        async () =>
        {
          await using var scope = previews.CreateAsyncScope();
          return await scope
            .ServiceProvider.GetRequiredService<RoutePreviewService>()
            .GetAsync(ct);
        }
      );
      Console.WriteLine(
        JsonSerializer.Serialize(
          new { kind = "fleet_preview_size", routes = fleet.Count }
        )
      );
    }

    await Measure(
      "hos_provider_read",
      async () =>
      {
        await using var scope = provider.CreateAsyncScope();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        var clocks = await scope
          .ServiceProvider.GetRequiredService<IDriverHosRefreshProvider>()
          .RefreshClocksAsync(limit.Token);
        provider.GetRequiredService<DriverHosSnapshot>().Complete(clocks);
        return new { drivers = clocks.Count };
      }
    );
    await transaction.RollbackAsync();

    ServiceProvider Build()
    {
      var services = new ServiceCollection();
      services.AddSingleton<IConfiguration>(config);
      services.AddLogging();
      services.AddApplication();
      services.AddInfrastructure(config);
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
      Bind<SynchronizationOptions>("Synchronization");
      Bind<FuelRegionOptions>("FuelRegions");
      Bind<RouteRecalculationBudgetOptions>("RouteRecalculationBudget");
      Bind<RoutePreparationOptions>("RoutePreparation");
      Bind<EtaPlanningOptions>("EtaPlanning");
      return services.BuildServiceProvider();
      void Bind<T>(string section)
        where T : class =>
        services
          .AddOptions<T>()
          .Bind(config.GetSection(section))
          .ValidateDataAnnotations();
    }

    async Task<T> Send<T>(IRequest<T> request)
    {
      await using var scope = provider.CreateAsyncScope();
      return await scope
        .ServiceProvider.GetRequiredService<ISender>()
        .Send(request, ct);
    }
    async Task<T> Measure<T>(string operation, Func<Task<T>> read)
    {
      sql.Values.Clear();
      var watch = Stopwatch.StartNew();
      var value = await read();
      watch.Stop();
      var serialize = Stopwatch.StartNew();
      var bytes = JsonSerializer.SerializeToUtf8Bytes(value).Length;
      serialize.Stop();
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            operation,
            elapsedMs = watch.Elapsed.TotalMilliseconds,
            dbCommands = sql.Values.Count,
            dbCommandMs = sql.Values.Sum(x => x.Ms),
            serializedBytes = bytes,
            serializeMs = serialize.Elapsed.TotalMilliseconds,
            tables = sql
              .Values.GroupBy(x => x.Tables)
              .Select(group => new
              {
                name = group.Key,
                count = group.Count(),
                ms = group.Sum(x => x.Ms),
              })
              .OrderByDescending(x => x.ms)
              .Take(8),
          }
        )
      );
      return value;
    }
  }

  private static async Task ReadPayloadsAsync(
    ServiceProvider provider,
    CancellationToken ct
  )
  {
    await using var scope = provider.CreateAsyncScope();
    var services = scope.ServiceProvider;
    var board = await services
      .GetRequiredService<ISender>()
      .Send(
        new GetDispatchBoardQuery(
          Date: DateOnly.FromDateTime(DateTime.Now),
          IncludeHos: false,
          IncludeEta: false,
          IncludeFinancials: false
        ),
        ct
      );
    foreach (var row in board.Response?.Items ?? [])
    {
      if (row.TruckId is not { } truck)
        continue;
      var preview = await services
        .GetRequiredService<RoutePreviewService>()
        .ForTruckAsync(truck, ct);
      var plan = preview.State?.Plan;
      Print("preview", row.TruckNumber, preview);
      var live = await services
        .GetRequiredService<PlanningReadService>()
        .ForTruckAsync(truck, ct, plan?.Id, plan?.Version);
      Print("known_version_planning", row.TruckNumber, live);
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            kind = "planning_payload_state",
            truck = row.TruckNumber,
            geometryOmitted = live.State?.Plan?.GeometryOmitted,
            hasFuel = live.State?.Plan?.FuelPlan is not null,
            hasEta = live.State?.Eta is not null,
          }
        )
      );
    }

    static void Print<T>(string kind, string? truck, T value)
    {
      var bytes = JsonSerializer.SerializeToUtf8Bytes(
        value,
        RoutePlanningService.Json
      );
      using var gzip = new MemoryStream();
      using (
        var encoder = new GZipStream(
          gzip,
          CompressionLevel.Fastest,
          leaveOpen: true
        )
      )
        encoder.Write(bytes);
      using var brotli = new MemoryStream();
      using (
        var encoder = new BrotliStream(
          brotli,
          CompressionLevel.Fastest,
          leaveOpen: true
        )
      )
        encoder.Write(bytes);
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            kind,
            truck,
            jsonBytes = bytes.Length,
            gzipFastestBytes = gzip.Length,
            brotliFastestBytes = brotli.Length,
          }
        )
      );
    }
  }

  private sealed class Commands
    : IObserver<DiagnosticListener>,
      IObserver<KeyValuePair<string, object?>>,
      IDisposable
  {
    private readonly List<IDisposable> subscriptions = [];
    public List<(string Tables, double Ms)> Values { get; } = [];

    public void OnNext(DiagnosticListener value)
    {
      if (value.Name == "Microsoft.EntityFrameworkCore")
        subscriptions.Add(value.Subscribe(this));
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
      if (value.Value is not CommandExecutedEventData command)
        return;
      var tables = Regex
        .Matches(
          command.Command.CommandText,
          "(?:FROM|JOIN)\\s+\"([A-Za-z]+)\""
        )
        .Select(match => match.Groups[1].Value)
        .Distinct()
        .Order();
      Values.Add(
        (string.Join('+', tables), command.Duration.TotalMilliseconds)
      );
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Dispose()
    {
      foreach (var item in subscriptions)
        item.Dispose();
    }
  }
}
