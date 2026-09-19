using System.Security.Cryptography;
using System.Text;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace CoreMigrationProbe;

internal static class CleanTransitionProbe
{
  private const string PreviousMigration = "20260914214701_AddStopCorrections";
  private static readonly HashSet<string> Protected =
  [
    "Users",
    "AspNetUsers",
    "AspNetUserClaims",
    "AspNetUserLogins",
    "AspNetUserTokens",
    "AspNetUserRoles",
    "AspNetRoles",
    "AspNetRoleClaims",
    "DataProtectionKeys",
    "IntegrationCredentialSettings",
    "__EFMigrationsHistory",
  ];

  public static async Task VerifyAsync(AppDbContext db)
  {
    await db.GetService<IMigrator>().MigrateAsync();
    await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
    Console.WriteLine("Empty schema upgrade and downgrade passed.");
    var account = new AppUser
    {
      Id = Guid.NewGuid().ToString(),
      UserName = "fixture@example.invalid",
      NormalizedUserName = "FIXTURE@EXAMPLE.INVALID",
      Email = "fixture@example.invalid",
      NormalizedEmail = "FIXTURE@EXAMPLE.INVALID",
      EmailConfirmed = true,
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
      TwoFactorEnabled = true,
    };
    const string password = "Synthetic fixture password only";
    account.PasswordHash = new PasswordHasher<AppUser>().HashPassword(
      account,
      password
    );
    var role = new IdentityRole("Fixture") { NormalizedName = "FIXTURE" };
    db.Set<AppUser>().Add(account);
    db.Roles.Add(role);
    db.UserRoles.Add(new() { UserId = account.Id, RoleId = role.Id });
    db.RoleClaims.Add(
      new()
      {
        RoleId = role.Id,
        ClaimType = "fixture",
        ClaimValue = "retained",
      }
    );
    db.UserClaims.Add(
      new()
      {
        UserId = account.Id,
        ClaimType = "amftms:role",
        ClaimValue = "Admin",
      }
    );
    db.UserLogins.Add(
      new()
      {
        UserId = account.Id,
        LoginProvider = "fixture",
        ProviderKey = "subject",
      }
    );
    db.UserTokens.Add(
      new()
      {
        UserId = account.Id,
        LoginProvider = "fixture",
        Name = "key",
        Value = "synthetic-value",
      }
    );
    db.Users.Add(
      new()
      {
        Id = Guid.NewGuid(),
        IdentityUserId = account.Id,
        Name = "Fixture profile",
        Email = account.Email,
        Theme = "dark",
        TemperatureUnit = "celsius",
        DistanceUnit = "kilometers",
      }
    );
    db.DataProtectionKeys.Add(
      new DataProtectionKey
      {
        FriendlyName = "fixture",
        Xml = "<fixture>retained</fixture>",
      }
    );
    db.IntegrationCredentialSettings.Add(
      new()
      {
        Provider = "fixture",
        ProtectedValues = "synthetic-ciphertext",
        Revision = 7,
        UpdatedAt = DateTime.UtcNow,
      }
    );
    var oldTruck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "old",
      UnitNumber = "old",
    };
    db.Trucks.Add(oldTruck);
    var oldTrip = new Trip { Id = Guid.NewGuid(), RecordedBy = Guid.Empty };
    db.Trips.Add(oldTrip);
    db.Dispatches.Add(
      new Load
      {
        Id = Guid.NewGuid(),
        TruckId = oldTruck.Id,
        Status = "assigned",
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Notes = "Old work",
          },
        ],
      }
    );
    await db.SaveChangesAsync();
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "ExecutionVisits" (
        "Id", "TripId", "Operation", "SiteName", "Latitude", "Longitude",
        "Revision"
      ) VALUES ({Guid.NewGuid()}, {oldTrip.Id}, 'Drop', 'Old site', 40, -80, 1)
      """
    );
    db.ChangeTracker.Clear();
    var before = await ProtectedDigest(db);
    await RejectMigration(db, null, "Clean operational reset required");
    var tables = await Tables(db);
    var mapped = db
      .Model.GetRelationalModel()
      .Tables.Select(x => x.Name)
      .ToHashSet();
    Require(
      tables.All(x =>
        Protected.Contains(x) || mapped.Contains(x) || x == "ExecutionVisits"
      ),
      "Unexpected tables need an explicit reset inventory."
    );
    var reset = tables.Where(x => !Protected.Contains(x)).ToArray();
    await VerifyResetScript(db);
    Require(
      before == await ProtectedDigest(db),
      "Operational cleanup changed protected identity or configuration."
    );
    await db.Database.MigrateAsync();
    Require(
      !(await Tables(db)).Contains("ExecutionVisits"),
      "Transfer visits must use the common accepted visit storage."
    );
    Require(
      before == await ProtectedDigest(db),
      "Schema transition changed protected identity or configuration."
    );
    Require(
      !db.Database.HasPendingModelChanges(),
      "The clean schema must match the application model."
    );
    Require(
      !await db.Trucks.AnyAsync()
        && !await db.Dispatches.AnyAsync()
        && !await db.DispatchStops.AnyAsync(),
      "Old operational facts must not survive the explicit reset."
    );
    var restored = await db.Set<AppUser>().SingleAsync();
    Require(
      new PasswordHasher<AppUser>().VerifyHashedPassword(
        restored,
        restored.PasswordHash!,
        password
      ) == PasswordVerificationResult.Success,
      "The existing identity password must remain valid."
    );
    Require(
      await new UserRoleService(db).GetAsync(account.Id) == "Admin",
      "Existing application roles must remain effective."
    );
    await DispatchIndependenceProbe.VerifyAsync(db);
    await VerifyNewWork(db);
    await ExecutionPlanningProbe.VerifyAsync(db);
    await PlanningRefreshProbe.VerifyAsync(db);
    await ExecutionAcceptanceProbe.VerifyAsync(db);
    await InitialAssignmentProbe.VerifyAsync(db);
    await ExecutionImportProbe.VerifyAsync(db);
    await BaseRoadPublicationProbe.VerifyAsync(db);
    await TransferAcceptanceProbe.VerifyAsync(db);
    await SourceRoadProbe.VerifyAsync(db);
    await PlanningInputRevisionProbe.VerifyAsync(db);
    await PlanningPublicationProbe.VerifyAsync(db);
    await RejectMigration(db, PreviousMigration, "Accepted execution requires");
    Console.WriteLine(
      $"PostgreSQL clean transition passed: {reset.Length} "
        + "operational tables reset; protected identity/configuration unchanged; "
        + "password, role, accepted order, transfer ownership, immutable history "
        + "and migration guards verified."
    );
  }

  private static async Task VerifyResetScript(AppDbContext db)
  {
    var script = await File.ReadAllTextAsync(
      Path.Combine(AppContext.BaseDirectory, "reset-core-storage.sql")
    );
    await db.Database.OpenConnectionAsync();
    try
    {
      async Task ExecuteScript()
      {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = script;
        command.CommandTimeout = 90;
        await command.ExecuteNonQueryAsync();
      }
      async Task Reject(string reason)
      {
        var rejected = false;
        try
        {
          await ExecuteScript();
        }
        catch (PostgresException ex) when (ex.MessageText.StartsWith(reason))
        {
          rejected = true;
          await db.Database.ExecuteSqlRawAsync("ROLLBACK");
        }
        Require(
          rejected && await db.Dispatches.AnyAsync(),
          "Reset preconditions must reject without deleting work."
        );
      }
      await Reject("Explicit target");
      await db.Database.ExecuteSqlRawAsync(
        """
        SELECT set_config('pulsr.reset_database', current_database(), false),
          set_config('pulsr.reset_ack',
            '20260917055902_RebuildExecutionStorage', false),
          set_config('pulsr.reset_writers_stopped', 'true', false),
          set_config('pulsr.reset_backup_verified', 'true', false)
        """
      );
      await using (
        var other = new NpgsqlConnection(db.Database.GetConnectionString())
      )
      {
        await other.OpenAsync();
        await Reject("Other database clients");
        NpgsqlConnection.ClearPool(other);
      }
      await db.Database.ExecuteSqlRawAsync(
        "CREATE TABLE reset_unknown_fixture (id integer)"
      );
      await Reject("Database tables differ");
      await db.Database.ExecuteSqlRawAsync("DROP TABLE reset_unknown_fixture");
      await ExecuteScript();
    }
    finally
    {
      await db.Database.CloseConnectionAsync();
    }
    Console.WriteLine(
      "Reviewed reset script passed: missing approval, connected clients and "
        + "unknown tables reject without changes; exact protected rows survive."
    );
  }

  private static async Task VerifyNewWork(AppDbContext db)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "new",
      UnitNumber = "new",
    };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      Trip = new() { Id = Guid.NewGuid() },
      Revision = 1,
    };
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    ExecutionStopRows.Replace(
      leg,
      [
        new()
        {
          Id = first,
          Sequence = 20,
          Notes = "Accepted first",
          Latitude = 35.0000m,
          ManualCompletedAt = null,
          ExecutionCompleted = true,
        },
        new()
        {
          Id = second,
          Sequence = 10,
          Notes = "Accepted second",
        },
      ]
    );
    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      db.Trucks.Add(truck);
      db.ExecutionLegs.Add(leg);
      await ExecutionHistory.RecordAsync(
        db,
        [leg],
        "source-synchronized",
        null,
        null,
        DateTime.UtcNow,
        default
      );
      await db.SaveChangesAsync();
      await transaction.CommitAsync();
    }
    db.ChangeTracker.Clear();
    var stored = await db.ExecutionLegs.SingleAsync();
    var visits = ExecutionStopRows.Read(stored);
    Require(
      visits.Select(x => x.Id).SequenceEqual([first, second])
        && visits.Select(x => x.Sequence).SequenceEqual([1, 2])
        && visits[0].ExecutionCompleted
        && visits[0].ManualCompletedAt is null,
      "New work must preserve accepted order and unknown actual time."
    );
    var history = await db.ExecutionLegRevisions.SingleAsync();
    Require(
      history.RecordedBy is null
        && history.Revision == 1
        && ExecutionRevisionFacts
          .Read(history)
          .Stops.Select(x => x.Id)
          .SequenceEqual([first, second]),
      "New imported acceptance must record its own immutable version."
    );
    await VerifyTransferOwnership(db);
    foreach (
      var sql in new[]
      {
        "UPDATE \"ExecutionLegRevisions\" SET \"Operation\" = 'rewrite'",
        "DELETE FROM \"ExecutionLegRevisions\"",
      }
    )
    {
      var rejected = false;
      try
      {
        await db.Database.ExecuteSqlRawAsync(sql);
      }
      catch (PostgresException ex) when (ex.SqlState == "P0001")
      {
        rejected = true;
      }
      Require(rejected, "New accepted history must be immutable.");
    }
  }

  private static async Task VerifyTransferOwnership(AppDbContext db)
  {
    var truck = await db.Trucks.SingleAsync();
    var actor = (await db.Users.SingleAsync()).Id;
    var load = new Load { Id = Guid.NewGuid(), LoadNumber = 1 };
    var legs = new[] { "Drop", "Hook" }
      .Select(operation => new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        TruckId = truck.Id,
        Trip = new() { Id = Guid.NewGuid() },
        Status = operation == "Drop" ? "completed" : "planned",
        Revision = 1,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = load.Id,
            Job = operation,
            Name = "Accepted site",
            Latitude = 41,
            Longitude = -80,
          },
        ],
      })
      .ToArray();
    for (var index = 0; index < legs.Length; index++)
      legs[index]
        .Loads.Add(
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = load.Id,
            Sequence = index,
            StartVisitId = legs[index].Stops[0].Id,
            EndVisitId = legs[index].Stops[0].Id,
          }
        );
    var operation = new DispatchSwitchOperation
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
      RecordedBy = actor,
      Status = "in_progress",
      Revision = 2,
    };
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      Switch = operation,
      DispatchId = load.Id,
      OutgoingLegId = legs[0].Id,
      IncomingLegId = legs[1].Id,
      ReleaseVisitId = legs[0].Stops[0].Id,
      ReceiveVisitId = legs[1].Stops[0].Id,
      ReleasedBy = actor,
      Revision = 2,
    };
    legs[0].EndSwitchId = operation.Id;
    legs[1].StartSwitchId = operation.Id;
    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      db.Dispatches.Add(load);
      db.ExecutionLegs.AddRange(legs);
      db.SwitchParticipants.Add(participant);
      await ExecutionHistory.RecordAsync(
        db,
        legs,
        "transfer-release",
        actor,
        null,
        DateTime.UtcNow,
        default
      );
      await db.SaveChangesAsync();
      await transaction.CommitAsync();
    }
    db.ChangeTracker.Clear();
    var ids = legs.Select(x => x.Id).ToArray();
    var saved = await db
      .ExecutionLegs.Where(x => ids.Contains(x.Id))
      .ToListAsync();
    var facts = await ExecutionTransfers.ReadAsync(db, saved, default);
    Require(
      facts[participant.ReleaseVisitId].ConfirmedBy == actor
        && facts[participant.ReleaseVisitId].ActualAt is null
        && facts[participant.ReleaseVisitId].Latitude == 41
        && facts[participant.ReceiveVisitId].ConfirmedBy is null,
      "A confirmed release with unknown time cannot confirm receipt."
    );
    var revision = await db.ExecutionLegRevisions.SingleAsync(x =>
      x.ExecutionLegId == legs[0].Id
    );
    Require(
      ExecutionRevisionFacts.Read(revision).Transfers.Single().ConfirmedBy
        == actor,
      "Transfer history must capture the participant in the same transaction."
    );
  }

  private static async Task RejectMigration(
    AppDbContext db,
    string? target,
    string reason
  )
  {
    var before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    var rejected = false;
    try
    {
      await db.GetService<IMigrator>().MigrateAsync(target);
    }
    catch (PostgresException ex)
      when (ex.SqlState == "P0001" && ex.MessageText.StartsWith(reason))
    {
      rejected = true;
    }
    Require(
      rejected
        && before.SequenceEqual(await db.Database.GetAppliedMigrationsAsync()),
      "An unsafe schema transition must reject without advancing migration state."
    );
  }

  private static async Task<string> ProtectedDigest(AppDbContext db)
  {
    using var identifiers = new NpgsqlCommandBuilder();
    var parts = new StringBuilder();
    foreach (
      var table in (await Tables(db))
        .Where(Protected.Contains)
        .Where(x => x != "__EFMigrationsHistory")
    )
    {
      var name = identifiers.QuoteIdentifier(table);
      var sql = $"""
        SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),
          '[]'::jsonb)::text AS "Value" FROM {name} t
        """;
      parts.Append(table);
      parts.Append(await db.Database.SqlQueryRaw<string>(sql).SingleAsync());
    }
    return Convert.ToHexString(
      SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString()))
    );
  }

  private static Task<List<string>> Tables(AppDbContext db) =>
    db
      .Database.SqlQueryRaw<string>(
        """
        SELECT tablename AS "Value" FROM pg_tables
        WHERE schemaname = 'public' ORDER BY tablename
        """
      )
      .ToListAsync();

  private static void Require(bool condition, string message)
  {
    if (!condition)
      throw new InvalidOperationException(message);
  }
}
