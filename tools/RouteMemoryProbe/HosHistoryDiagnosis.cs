using System.Text.Json;
using Application;
using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Interfaces;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class HosHistoryDiagnosis
{
  public static async Task RunAsync(string truckNumber)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var ct = timeout.Token;
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
    await connection.OpenAsync(ct);
    await using var transaction = await connection.BeginTransactionAsync(ct);
    await using (
      var settings = new NpgsqlCommand(
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='10s'",
        connection,
        transaction
      )
    )
      await settings.ExecuteNonQueryAsync(ct);
    await using var query = new NpgsqlCommand(
      """
      SELECT d."ExternalId" FROM "Trucks" t JOIN "Drivers" d ON d."Id"=t."DriverId"
      WHERE t."UnitNumber"=@unit LIMIT 1
      """,
      connection,
      transaction
    );
    query.Parameters.AddWithValue("unit", truckNumber);
    var driver = (string?)await query.ExecuteScalarAsync(ct);
    if (string.IsNullOrWhiteSpace(driver))
      throw new InvalidOperationException(
        "The selected truck has no driver mapping."
      );

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
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var history = await scope
      .ServiceProvider.GetRequiredService<IHosHistoryProvider>()
      .GetAsync(driver, ct);
    if (history is null)
      Console.WriteLine(
        JsonSerializer.Serialize(new { truckNumber, historyAvailable = false })
      );
    else
    {
      var now = DateTimeOffset.UtcNow;
      var timeline = HosTimeline.Create(history, now);
      var cursor = history.From;
      string? previousStatus = null;
      var gaps = new List<double>();
      var conflictingOverlaps = 0;
      foreach (var period in history.Periods.OrderBy(x => x.Start))
      {
        if (period.Start > cursor.AddSeconds(1))
          gaps.Add((period.Start - cursor).TotalMinutes);
        if (
          previousStatus is not null
          && period.Start < cursor
          && previousStatus != period.Status
        )
          conflictingOverlaps++;
        if (period.End <= cursor)
          continue;
        cursor = period.End;
        previousStatus = period.Status;
      }
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            truckNumber,
            historyAvailable = true,
            history.From,
            history.Through,
            history.TimeZoneId,
            history.DayStartHour,
            history.UsCycle,
            history.CanadaCycle,
            periods = history.Periods.Count,
            timelineAccepted = timeline is not null,
            missingStartMinutes = history.Periods.Count == 0
              ? (double?)null
              : (history.Periods.Min(x => x.Start) - history.From).TotalMinutes,
            missingEndMinutes = (history.Through - cursor).TotalMinutes,
            gapCount = gaps.Count,
            largestGapMinutes = gaps.Count == 0 ? 0 : gaps.Max(),
            conflictingOverlaps,
            unknownStatuses = history
              .Periods.Select(x => x.Status)
              .Distinct()
              .Where(x =>
                x
                  is not (
                    "driving"
                    or "onDuty"
                    or "yardMove"
                    or "offDuty"
                    or "sleeperBerth"
                    or "personalConveyance"
                  )
              )
              .ToArray(),
            usWindowCovered = timeline is not null
              && history.UsCycle is { } rule
              && double.IsFinite(timeline.CycleUsed(now, rule)),
          },
          new JsonSerializerOptions { WriteIndented = true }
        )
      );
    }
    await transaction.RollbackAsync(ct);
  }
}
