using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class SwappedAssignmentRepair
{
  public static async Task RunAsync(string[] args)
  {
    var apply = args.Contains("--apply");
    if (apply == args.Contains("--read-only"))
      throw new InvalidOperationException("Choose --read-only or --apply.");
    var expected = args.FirstOrDefault(x => x.StartsWith("--fingerprint="))
      ?.Split('=', 2)[1];
    if (apply && string.IsNullOrWhiteSpace(expected))
      throw new InvalidOperationException(
        "Apply requires a preflight fingerprint."
      );

    var targets = new Dictionary<Guid, (int Load, string Truck)>
    {
      [Guid.Parse("360b616c-aae1-4a9f-acf2-58f82f72eb34")] = (1376, "11007"),
      [Guid.Parse("fdb9bafe-72e5-4013-8a52-2c29f2e5ccff")] = (1383, "54777"),
    };
    var config = new ConfigurationBuilder()
      .AddUserSecrets("pulsartms-api-local")
      .Build();
    var connection =
      config.GetConnectionString("DefaultConnection")
      ?? throw new InvalidOperationException("Database configuration missing.");
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connection, options => options.CommandTimeout(10))
        .Options
    );
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var ct = timeout.Token;
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var ids = targets.Keys.ToArray();
    var loads = await db
      .Dispatches.Include(x => x.Stops)
      .Where(x => ids.Contains(x.Id))
      .OrderBy(x => x.Id)
      .ToListAsync(ct);
    var numbers = targets.Values.Select(x => x.Truck).ToArray();
    var trucks = await db
      .Trucks.AsNoTracking()
      .Where(x => numbers.Contains(x.UnitNumber))
      .ToDictionaryAsync(x => x.UnitNumber, ct);
    if (loads.Count != 2 || trucks.Count != 2)
      throw new InvalidOperationException("Incident targets no longer match.");
    foreach (var load in loads)
    {
      var target = targets[load.Id];
      var truck = trucks[target.Truck];
      if (
        load.LoadNumber != target.Load
        || load.Status != "in_transit"
        || load.Stops.Count != 2
        || load.TruckId != truck.Id
        || load.Stops.Any(x =>
          x.TruckId != truck.Id || x.TruckNumber != target.Truck
        )
      )
        throw new InvalidOperationException(
          "Assignments changed; nothing saved."
        );
    }
    var before = loads
      .Select(load => new
      {
        load.Id,
        load.LoadNumber,
        load.TruckId,
        load.PlanningTruckId,
        load.PlanningFromStopId,
        load.PlanningAssignmentRevision,
        load.RouteChoiceRevision,
        stops = load
          .Stops.OrderBy(x => x.Id)
          .Select(x => new
          {
            x.Id,
            x.Sequence,
            x.TruckId,
            x.OperationRevision,
            x.ManualCompletionRevision,
          })
          .ToArray(),
      })
      .ToArray();
    var fingerprint = Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(before))
    );
    if (apply && fingerprint != expected)
      throw new InvalidOperationException("Preflight changed; nothing saved.");
    var changed = loads
      .Where(load =>
        DispatchAssignmentReconciliation.ReleaseStaleConfirmation(
          load,
          trucks,
          DateTime.UtcNow
        )
      )
      .Select(x => x.LoadNumber)
      .ToArray();
    foreach (var load in loads)
    {
      var itinerary = load.TruckItinerary();
      if (
        itinerary.TruckId != trucks[targets[load.Id].Truck].Id
        || itinerary.Stops.Count == 0
      )
        throw new InvalidOperationException(
          "Assignment still conflicts; not saved."
        );
    }
    if (apply)
    {
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    else
      await transaction.RollbackAsync(ct);
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          applied = apply,
          fingerprint,
          releasedLoads = changed,
          before,
        }
      )
    );
  }
}
