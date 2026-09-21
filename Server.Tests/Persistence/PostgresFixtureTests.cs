using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// The fixture borrows a shared database and separates runs by schema. When
// that separation silently failed, the tests using it wrote into the shared
// schema and read each other's rows: the first symptom was a count of ten
// where one was expected, which reads like a bug in the code under test
// rather than in the fixture. These ask the fixture to prove it.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class PostgresFixtureTests
{
  [RequiresPostgresFact]
  public async Task TheRunGetsItsOwnSchemaAndTheModelIsInIt()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();

    var schema = await ScalarAsync<string>(db, "SELECT current_schema()");
    Assert.StartsWith("t_", schema);
    Assert.Equal(
      schema,
      await ScalarAsync<string>(
        db,
        """
        SELECT table_schema FROM information_schema.tables
        WHERE table_name = 'PlanningRefreshRequests'
          AND table_schema = current_schema()
        """
      )
    );
  }

  [RequiresPostgresFact]
  public async Task WhatARunWritesDoesNotReachTheSharedSchema()
  {
    await using var f = await PostgresFixture.CreateAsync();
    var db = f.Connect();
    var before = await SharedCountAsync(db);

    await db.Database.ExecuteSqlRawAsync(
      """
      INSERT INTO "PlanningRefreshRequests" (
        "Id", "CompanyId", "DispatchId", "ExecutionLegId",
        "AssignmentRevision",
        "InputSignature", "RequestedVersion", "CompletedVersion",
        "RequestedAt", "AvailableAt", "Attempts"
      ) VALUES (
        'isolation-probe', 'a0f0a0f0-0000-4000-8000-000000000001',
        gen_random_uuid(), NULL, 0, 'probe', 1, 0,
        now(), now(), 0
      )
      """
    );

    Assert.Equal(1, await db.PlanningRefreshRequests.CountAsync());
    Assert.Equal(before, await SharedCountAsync(db));
  }

  private static async Task<long> SharedCountAsync(
    Infrastructure.Persistence.AppDbContext db
  ) =>
    await ScalarAsync<long>(
      db,
      """SELECT count(*) FROM public."PlanningRefreshRequests" """
    );

  private static async Task<T> ScalarAsync<T>(
    Infrastructure.Persistence.AppDbContext db,
    string sql
  )
  {
    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
      await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return (T)(await command.ExecuteScalarAsync())!;
  }
}
