using System.Security.Cryptography;
using System.Text.Json;
using Application;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure;
using Infrastructure.Integrations.Torque;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

try
{
  await RunAsync(args);
}
catch (Exception ex)
{
  Console.Error.WriteLine(
    JsonSerializer.Serialize(
      new
      {
        error = ex.GetType().Name,
        detail = "Import stopped; inspect its bounded stage summary.",
      }
    )
  );
  Environment.ExitCode = 1;
}

static async Task RunAsync(string[] args)
{
  var apply = args.Contains("--apply");
  var reconcile = args.Contains("--reconcile-invoiced");
  var historicalOnly = args.Contains("--historical-only");
  var refreshFloor = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
  string Value(string name) =>
    args.Single(x => x.StartsWith(name + "=")).Split('=', 2)[1];
  var from = DateOnly.ParseExact(Value("--from"), "yyyy-MM-dd");
  var to = DateOnly.ParseExact(Value("--to"), "yyyy-MM-dd");
  if (
    from > to
    || to > DateOnly.FromDateTime(DateTime.UtcNow)
    || to.DayNumber - from.DayNumber > 366
  )
    throw new InvalidOperationException("Invalid bounded import range.");
  if (apply)
  {
    var backup = Value("--backup");
    using var stream = File.OpenRead(backup);
    var hash = Convert.ToHexString(SHA256.HashData(stream));
    if (
      !hash.Equals(Value("--backup-sha256"), StringComparison.OrdinalIgnoreCase)
    )
      throw new InvalidOperationException("Backup hash mismatch.");
  }
  var config = new ConfigurationBuilder()
    .SetBasePath(Path.GetFullPath("Server/API"))
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json")
    .AddUserSecrets("pulsartms-api-local")
    .AddEnvironmentVariables()
    .AddInMemoryCollection(
      new Dictionary<string, string?>
      {
        ["DispatchImport:Provider"] = "torqueai",
        ["Synchronization:Enabled"] = "false",
        ["Database:ApplyMigrations"] = "false",
      }
    )
    .Build();
  var backupArgument = args.SingleOrDefault(x =>
    x.StartsWith("--create-backup=")
  );
  if (backupArgument is not null)
  {
    if (apply)
      throw new InvalidOperationException("Separate backup and apply.");
    await ImportBackup.CreateAsync(
      config.GetConnectionString("DefaultConnection")!,
      backupArgument.Split('=', 2)[1]
    );
    return;
  }
  var services = new ServiceCollection();
  services.AddSingleton<IConfiguration>(config);
  services.AddLogging(x => x.ClearProviders());
  services.AddApplication();
  services.AddInfrastructure(config);
  services.AddDataProtection().DisableAutomaticKeyGeneration();
  services.Configure<DispatchImportOptions>(
    config.GetSection("DispatchImport")
  );
  var syncOptions =
    config.GetSection("Synchronization").Get<SynchronizationOptions>() ?? new();
  var orderFloor = DateOnly
    .FromDateTime(DateTime.UtcNow)
    .AddDays(-syncOptions.DispatchLookbackDays);
  var batch = new ImportBatch();
  services.RemoveAll<IDispatchProvider>();
  services.AddSingleton<IDispatchProvider>(batch);
  await using var provider = services.BuildServiceProvider();
  using var company = provider
    .GetRequiredService<ICurrentCompany>()
    .As(Company.Amf);
  using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
  var ct = timeout.Token;
  if (args.Contains("--verify-completed-financials"))
  {
    await using var scope = provider.CreateAsyncScope();
    await HistoryVerification.ReadFinancialsAsync(scope.ServiceProvider, ct);
    return;
  }
  if (args.Contains("--verify-history"))
  {
    await using var scope = provider.CreateAsyncScope();
    await HistoryVerification.ReadAsync(scope.ServiceProvider, ct);
    return;
  }
  var seen = new HashSet<string>();
  var totalNew = 0;
  var totalExisting = 0;
  var totalRepairs = 0;
  var totalEligible = 0;
  var retired = new[] { "11", "11001", "11002", "11003", "11004" };
  var retiredCounts = new Dictionary<string, int>();
  for (var start = from; start <= to; start = start.AddDays(30))
  {
    var end = start.AddDays(29) < to ? start.AddDays(29) : to;
    await using var scope = provider.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<AppDbContext>();
    if (
      db.ServingCompany != Company.Amf
      || !await db.Companies.AnyAsync(
        x => x.Id == Company.Amf && x.IsActive,
        ct
      )
    )
      throw new InvalidOperationException("Company scope unavailable.");
    var source = new TorqueDispatchProvider(
      sp.GetRequiredService<TorqueApiService>()
    );
    var rows = await source.GetDispatchesAsync(start, end, ct);
    var unique = rows.Where(x => seen.Add(x.ExternalId)).ToArray();
    var ids = unique.Select(x => x.ExternalId).ToArray();
    var existing = await db
      .DispatchSourceLinks.AsNoTracking()
      .Where(x => x.Provider == "torqueai" && ids.Contains(x.ExternalId))
      .Select(x => new { x.ExternalId, x.Dispatch.Status })
      .ToListAsync(ct);
    var examples = new[] { 1016, 1059, 1133, 1172 };
    var examplesHere = unique
      .Where(x => examples.Contains(x.LoadNumber))
      .Select(x => x.ExternalId)
      .ToArray();
    if (examplesHere.Length > 0)
    {
      var exampleLoads = await db
        .DispatchSourceLinks.AsNoTracking()
        .Where(x =>
          x.Provider == "torqueai" && examplesHere.Contains(x.ExternalId)
        )
        .Select(x => new
        {
          x.ExternalId,
          x.DispatchId,
          x.Dispatch.Status,
        })
        .ToListAsync(ct);
      var exampleIds = exampleLoads.Select(x => x.DispatchId).ToArray();
      var exampleLegs = await db
        .LoadExecutionLegs.AsNoTracking()
        .Where(x => exampleIds.Contains(x.DispatchId))
        .Select(x => new { x.DispatchId, x.ExecutionLeg.Status })
        .ToListAsync(ct);
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            examples = exampleLoads.Select(x => new
            {
              number = x.ExternalId,
              sourceStatus = unique
                .Single(y => y.ExternalId == x.ExternalId)
                .Status,
              storedStatus = x.Status,
              execution = exampleLegs
                .Where(y => y.DispatchId == x.DispatchId)
                .Select(y => y.Status)
                .ToArray(),
            }),
          }
        )
      );
    }
    var known = existing
      .Select(x => x.ExternalId)
      .ToHashSet(StringComparer.Ordinal);
    var invoiced = existing
      .Where(x =>
        string.Equals(
          x.Status?.Trim(),
          "sent",
          StringComparison.OrdinalIgnoreCase
        )
      )
      .Select(x => x.ExternalId)
      .ToHashSet(StringComparer.Ordinal);
    var repairs = unique
      .Where(x => invoiced.Contains(x.ExternalId) && x.Status == "completed")
      .ToArray();
    var eligible = historicalOnly
      ? repairs
        .Where(x => x.DeliveryDate < refreshFloor || end < orderFloor)
        .ToArray()
      : repairs;
    var selected = reconcile
      ? eligible
      : unique.Where(x => !known.Contains(x.ExternalId)).ToArray();
    totalRepairs += repairs.Length;
    totalEligible += eligible.Length;
    foreach (var row in eligible.Where(x => retired.Contains(x.TruckNumber)))
      retiredCounts[row.TruckNumber] =
        retiredCounts.GetValueOrDefault(row.TruckNumber) + 1;
    var fresh = unique.Length - existing.Count;
    totalNew += fresh;
    totalExisting += existing.Count;
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          mode = apply ? "apply" : "preview",
          from = start,
          to = end,
          source = rows.Count,
          distinct = unique.Length,
          existing = existing.Count,
          missing = fresh,
          invoicedRepairs = repairs.Length,
          reconcile,
          eligibleRepairs = eligible.Length,
          historicalOnly,
          statuses = unique
            .GroupBy(x => x.Status)
            .ToDictionary(x => x.Key, x => x.Count()),
        }
      )
    );
    if (!apply || selected.Length == 0)
      continue;
    // The normal command owns identity, reconciliation and its transaction.
    // No host is started: this process runs no background jobs or migrations.
    batch.Items = selected;
    var result = await sp.GetRequiredService<ISender>()
      .Send(new SyncDispatchesCommand(), ct);
    if (!result.Success)
      throw new InvalidOperationException(
        $"Import stopped with status {result.StatusCode}; inspect owner result."
      );
    db.ChangeTracker.Clear();
    var linked = await db
      .DispatchSourceLinks.AsNoTracking()
      .Where(x => x.Provider == "torqueai" && ids.Contains(x.ExternalId))
      .Select(x => x.ExternalId)
      .Distinct()
      .CountAsync(ct);
    if (linked != unique.Length)
      throw new InvalidOperationException("Committed identity check failed.");
    if (reconcile)
    {
      var repairedIds = selected.Select(x => x.ExternalId).ToArray();
      var verified = await db
        .DispatchSourceLinks.AsNoTracking()
        .CountAsync(
          x =>
            x.Provider == "torqueai"
            && repairedIds.Contains(x.ExternalId)
            && x.Dispatch.Status == "completed",
          ct
        );
      if (verified != selected.Length)
        throw new InvalidOperationException("Completion check failed.");
    }
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          committedFrom = start,
          committedTo = end,
          verifiedLinks = linked,
          changedEntities = result.Response,
        }
      )
    );
  }
  Console.WriteLine(
    JsonSerializer.Serialize(
      new
      {
        completed = true,
        applied = apply,
        from,
        to,
        sourceIdentities = seen.Count,
        missingBefore = totalNew,
        existingBefore = totalExisting,
        invoicedRepairsBefore = totalRepairs,
        eligibleRepairsBefore = totalEligible,
        retiredTruckRepairs = retiredCounts,
      }
    )
  );
}

sealed class ImportBatch : IDispatchProvider
{
  public string Key => "torqueai";
  public string DisplayName => "TorqueAI";
  public IReadOnlyList<ExternalDispatch> Items { get; set; } = [];

  public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
    CancellationToken ct = default
  ) => Task.FromResult(Items);

  public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
    DateOnly from,
    DateOnly to,
    CancellationToken ct = default
  ) => Task.FromResult(Items);
}
