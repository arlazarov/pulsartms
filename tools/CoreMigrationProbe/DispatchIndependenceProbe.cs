using Application.Caching;
using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Routing.Background;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CoreMigrationProbe;

internal static class DispatchIndependenceProbe
{
  public static async Task VerifyAsync(AppDbContext db)
  {
    var identity = await db.Users.Select(x => x.IdentityUserId).SingleAsync();
    var caller = new Caller(identity);
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var queue = new RoutePreparationQueue(
      Options.Create(new RoutePreparationOptions()),
      TimeProvider.System
    );
    CreateDispatchHandler Creator(AppDbContext context) =>
      new(
        context,
        caller,
        new UserRoleService(context),
        TimeProvider.System,
        reads,
        queue
      );
    var request = Request();
    var first = await Creator(db).Handle(new(request), default);
    Require(first.Success, "Provider-free load creation must succeed.");
    var native = first.Response!.Load;
    var retry = await Creator(db).Handle(new(request), default);
    Require(
      retry.Success && retry.Response!.Load.Id == native.Id,
      "Creation retry must return the original load."
    );
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var source = new ExternalDispatch
    {
      ExternalId = "stable-source-key",
      LoadNumber = native.LoadNumber,
      OrderNumber = "Imported fixture",
    };
    var adapter = new Provider(source);
    var sync = new SyncDispatchesCommandHandler(
      db,
      [adapter],
      Options.Create(new DispatchImportOptions { Provider = adapter.Key }),
      reads,
      memory,
      queue
    );
    Require(
      (await sync.Handle(new(), default)).Success,
      "A colliding import must still create its own load."
    );
    var imported = await db
      .DispatchSourceLinks.Include(x => x.Dispatch)
      .SingleAsync();
    Require(
      imported.DispatchId != native.Id
        && imported.Dispatch.LoadNumber != native.LoadNumber,
      "External display numbers cannot take over native identity."
    );
    source.LoadNumber = 100;
    memory.Compact(1);
    Require(
      (await sync.Handle(new(), default)).Success
        && await db.Dispatches.CountAsync() == 2,
      "Changed external display numbers must not create duplicates."
    );
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseNpgsql(db.Database.GetConnectionString())
      .Options;
    var concurrent = await Task.WhenAll(
      Enumerable
        .Range(0, 4)
        .Select(async _ =>
        {
          var command = new CreateDispatchCommand(Request());
          for (var attempt = 0; attempt < 8; attempt++)
          {
            await using var context = new AppDbContext(options);
            var result = await Creator(context).Handle(command, default);
            if (result.Success)
              return result.Response!.Load.Id;
            Require(
              result.StatusCode == 409,
              "Only concurrency may request a retry."
            );
          }
          throw new InvalidOperationException(
            "Concurrent creation did not converge."
          );
        })
    );
    Require(
      concurrent.Distinct().Count() == 4
        && await db.Dispatches.Select(x => x.LoadNumber).Distinct().CountAsync()
          == 6,
      "Concurrent creation must retain distinct IDs and numbers."
    );
    var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    var rejected = false;
    try
    {
      await db.GetService<IMigrator>()
        .MigrateAsync("20260914214701_AddStopCorrections");
    }
    catch (PostgresException ex)
      when (ex.SqlState == "P0001"
        && ex.MessageText.StartsWith("Native dispatch requires")
      )
    {
      rejected = true;
    }
    Require(
      rejected
        && migrations.SequenceEqual(
          await db.Database.GetAppliedMigrationsAsync()
        ),
      "Downgrade must retain native loads and their source identities."
    );
    db.ChangeTracker.Clear();
    await db.DispatchWorkspaceRevisions.ExecuteDeleteAsync();
    await db.DispatchWorkspaces.ExecuteDeleteAsync();
    await db.DispatchSourceLinks.ExecuteDeleteAsync();
    await db.DispatchStops.ExecuteDeleteAsync();
    await db.Dispatches.ExecuteDeleteAsync();
    await db.DispatchNumberCounters.ExecuteDeleteAsync();
    Console.WriteLine(
      "Native creation, replay, import isolation, concurrent numbering and downgrade guard passed."
    );
  }

  internal static CreateDispatchRequest Request() =>
    new()
    {
      IdempotencyKey = Guid.NewGuid(),
      OrderNumber = "Native fixture",
      Stops = new[] { "Pick Up", "Delivery" }
        .Select(
          (job, i) =>
            new DispatchWorkspaceStop
            {
              Id = Guid.NewGuid(),
              Sequence = i + 1,
              Job = job,
              Name = "Fixture facility",
              Address = "100 Fixture Street",
              City = "Toronto",
              Province = "ON",
              Country = "CA",
              Latitude = 43.65m,
              Longitude = -79.38m,
            }
        )
        .ToList(),
    };

  private sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => identity;
  }

  private sealed class Provider(ExternalDispatch source) : IDispatchProvider
  {
    public string Key => "fixture";
    public string DisplayName => "Fixture import";

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<ExternalDispatch>>([source]);

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      DateOnly from,
      DateOnly to,
      CancellationToken ct = default
    ) => GetDispatchesAsync(ct);
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
