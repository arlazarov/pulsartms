using System.Text.RegularExpressions;
using Application.Features.Border;
using Application.Features.Border.Models;
using Application.Features.Shipments;
using Application.Features.Shipments.Models;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Load = Domain.Entities.Dispatch.Dispatch;

const string migration = "20260918120545_AddShipmentAndBorderDrafts";
var mode = args.SingleOrDefault();
var connection = new NpgsqlConnectionStringBuilder(
  Environment.GetEnvironmentVariable("PULSR_PREPARATION_CONNECTION") ?? ""
);
var development = mode == "--apply-development";
if (development)
{
  if (
    connection.Database != "pulsr_development"
    || connection.Username != "pulsr_developer"
  )
    throw new InvalidOperationException(
      "Only the isolated development database is allowed."
    );
}
else if (
  mode != "--fixture"
  || connection.Username != "pulsr_test_runner"
  || !Regex.IsMatch(
    connection.Database ?? "",
    "^pulsr_core_fixture_[0-9a-f]{32}$"
  )
)
  throw new InvalidOperationException(
    "Choose the dedicated preparation fixture."
  );
var options = new DbContextOptionsBuilder<AppDbContext>()
  .UseNpgsql(connection.ConnectionString)
  .Options;
await using var db = new AppDbContext(options);
if (development)
{
  var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
  if (pending.Length != 0 && !pending.SequenceEqual([migration]))
    throw new InvalidOperationException(
      "Unexpected pending migrations; no change was applied."
    );
  await db.Database.MigrateAsync();
  Console.WriteLine(
    "Development preparation migration applied; no data seeded."
  );
  return;
}
var tables = await db
  .Database.SqlQueryRaw<int>(
    """
    SELECT count(*)::integer AS "Value" FROM information_schema.tables
    WHERE table_schema = 'public'
    """
  )
  .SingleAsync();
if (tables != 0)
  throw new InvalidOperationException("The fixture must initially be empty.");
try
{
  await db.Database.MigrateAsync();
  var actor = new User
  {
    Id = Guid.NewGuid(),
    IdentityUserId = "border-fixture",
    Name = "Synthetic operator",
    Email = "border@example.invalid",
  };
  var load = new Load { Id = Guid.NewGuid(), LoadNumber = 991001 };
  db.Users.Add(actor);
  db.Dispatches.Add(load);
  await db.SaveChangesAsync();
  var shipmentHandler = new ShipmentsHandler(
    db,
    new Caller(),
    new Roles(),
    TimeProvider.System
  );
  var shipment = new Shipment
  {
    Id = Guid.NewGuid(),
    LoadId = load.Id,
    BillOfLading = "SYNTHETIC-BOL",
    Commodities =
    [
      new()
      {
        Id = Guid.NewGuid(),
        Description = "Synthetic boxes",
        Quantity = 5,
        PackageType = "box",
        Weight = 100,
        WeightUnit = "lb",
      },
    ],
  };
  var savedShipment = await shipmentHandler.Handle(
    new SaveShipmentCommand(new(Guid.NewGuid(), 0, shipment)),
    default
  );
  Require(savedShipment.Success, "Shipment save");
  var protection = new BorderDataProtection(
    new EphemeralDataProtectionProvider()
  );
  var handler = new SaveBorderCrossingHandler(
    db,
    new Caller(),
    new Roles(),
    TimeProvider.System,
    protection
  );
  var draft = new BorderCrossing
  {
    Id = Guid.NewGuid(),
    DestinationCountry = "CA",
    Shipments =
    [
      new()
      {
        Id = Guid.NewGuid(),
        ShipmentId = shipment.Id,
        ShipmentRevision = 1,
        Procedure = "PARS",
        ParsNumber = "000TEST",
      },
    ],
    Crew =
    [
      new()
      {
        Id = Guid.NewGuid(),
        Role = "passenger",
        Details = new() { FirstName = "Synthetic", LastName = "Person" },
      },
    ],
  };
  var request = new SaveBorderCrossing(Guid.NewGuid(), 0, draft);
  var saved = await handler.Handle(new(request), default);
  Require(saved.Success, "Crossing save");
  Require(
    (await handler.Handle(new(request), default)).Success,
    "Idempotent replay"
  );
  db.ChangeTracker.Clear();
  await using var fresh = new AppDbContext(options);
  var read = await new BorderQueries(
    fresh,
    new Caller(),
    new Roles(),
    protection
  ).Handle(new GetBorderCrossing(draft.Id), default);
  Require(
    read.Success
      && read.Response!.Shipments[0].Snapshot!.BillOfLading == "SYNTHETIC-BOL",
    "Fresh PostgreSQL snapshot read"
  );
  Require(
    read.Response!.Crew[0].Details.FirstName == "Synthetic",
    "Protected crew roundtrip"
  );
  var stale = await new SaveBorderCrossingHandler(
    fresh,
    new Caller(),
    new Roles(),
    TimeProvider.System,
    protection
  ).Handle(new(request with { RequestId = Guid.NewGuid() }), default);
  Require(stale.StatusCode == 409, "Stale revision rejection");
  Console.WriteLine(
    "PostgreSQL migration, shipment save, crossing snapshot, protected crew, replay and stale-save checks passed."
  );
}
finally
{
  // This schema was empty before this probe and contains only its own fixture.
  await db.Database.ExecuteSqlRawAsync(
    "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"
  );
  Console.WriteLine("Dedicated fixture returned to an empty schema.");
}

static void Require(bool condition, string operation)
{
  if (!condition)
    throw new InvalidOperationException(operation + " failed.");
}

sealed class Caller : ICurrentUser
{
  public bool IsAuthenticated => true;
  public string IdentityUserId => "border-fixture";
}

sealed class Roles : IUserRoleService
{
  public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
    Task.FromResult<string?>("Admin");

  public Task<Dictionary<Guid, string>> GetAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct = default
  ) => throw new NotSupportedException();

  public Task SetAsync(
    string id,
    string value,
    CancellationToken ct = default
  ) => throw new NotSupportedException();
}
