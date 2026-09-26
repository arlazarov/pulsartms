using System.Security.Cryptography;
using System.Text.Json;
using Application;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
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
  var services = new ServiceCollection();
  services.AddSingleton<IConfiguration>(config);
  services.AddLogging(x => x.ClearProviders());
  services.AddApplication();
  services.AddInfrastructure(config);
  services.AddDataProtection().DisableAutomaticKeyGeneration();
  services.Configure<DispatchImportOptions>(
    config.GetSection("DispatchImport")
  );
  var batch = new ImportBatch();
  services.RemoveAll<IDispatchProvider>();
  services.AddSingleton<IDispatchProvider>(batch);
  await using var provider = services.BuildServiceProvider();
  using var company = provider
    .GetRequiredService<ICurrentCompany>()
    .As(Company.Amf);
  using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
  var ct = timeout.Token;
  var seen = new HashSet<string>();
  var totalNew = 0;
  var totalExisting = 0;
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
      .Select(x => x.ExternalId)
      .ToListAsync(ct);
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
          statuses = unique
            .GroupBy(x => x.Status)
            .ToDictionary(x => x.Key, x => x.Count()),
        }
      )
    );
    if (!apply || fresh == 0)
      continue;
    // The normal command owns identity, reconciliation and its transaction.
    // No host is started: this process runs no background jobs or migrations.
    var known = existing.ToHashSet(StringComparer.Ordinal);
    batch.Items = unique.Where(x => !known.Contains(x.ExternalId)).ToArray();
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
