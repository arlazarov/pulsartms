using System.Data.Common;
using System.Diagnostics;
using Application.Features.Messaging.Queries;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Server.Tests.Messaging;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Persistence;

// What a search costs PostgreSQL, measured on the isolated fixture: a
// company with 100,000 messages in 200 conversations beside another with
// as many. Each statement the search sends is run again under EXPLAIN
// (ANALYZE, BUFFERS) and printed; the test checks the answers, not the
// timings, which are recorded in docs/features/driver-messaging.md.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class MessageSearchPostgresTests(ITestOutputHelper output)
{
  private const int Conversations = 200;
  private const int PerConversation = 500;

  [RequiresPostgresFact]
  public async Task SearchCostIsMeasuredOnALargeHistory()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var other = Guid.NewGuid();
    var db = fixture.Connect();
    var user = Guid.NewGuid();
    db.Users.Add(
      new User
      {
        Id = user,
        IdentityUserId = "me",
        Name = "Me",
        Email = "me@example.invalid",
        IsActive = true,
      }
    );
    await db.SaveChangesAsync();
    foreach (var company in new[] { Company.Amf, other })
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"""
        INSERT INTO "Conversations" ("Id", "CompanyId", "Channel",
          "BusinessNumberId", "Participant", "LastMessageAt", "LastPreview",
          "Revision", "LastInboundRevision", "LastInboundSequence")
        SELECT gen_random_uuid(), {company}, 'whatsapp', '123456',
          '+1555' || lpad(g::text, 7, '0'), now(), '', 1, 0, 0
        FROM generate_series(1, {Conversations}) g;
        INSERT INTO "ConversationMessages" ("Id", "CompanyId",
          "ConversationId", "Channel", "BusinessNumberId", "Direction", "Kind",
          "Body", "Attempt", "Status", "StatusAt", "SentAt", "CreatedAt",
          "ArrivedRevision", "Fence")
        SELECT gen_random_uuid(), {company}, c."Id", 'whatsapp', '123456',
          CASE WHEN n % 3 = 0 THEN 'out' ELSE 'in' END, 'text',
          'Status update ' || n || ' for load ' || (1000 + n % 400),
          0, 'received', now(), now() - (n || ' minutes')::interval,
          now() - (n || ' minutes')::interval, 0, 0
        FROM "Conversations" c, generate_series(1, {PerConversation}) n
        WHERE c."CompanyId" = {company};
        """
      );
    // One rare word, deep in one conversation's history.
    await db.Database.ExecuteSqlRawAsync(
      """
      UPDATE "ConversationMessages" SET "Body" = 'Tarpaulin torn at the rear'
      WHERE "Id" = (SELECT "Id" FROM "ConversationMessages"
        WHERE "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001'
        ORDER BY "SentAt" LIMIT 1);
      INSERT INTO "MessageAttachments" ("Id", "CompanyId", "MessageId",
        "OriginalName", "DeclaredType", "Caption", "State", "Attempts",
        "NextAttemptAt",
        "CreatedAt")
      SELECT gen_random_uuid(), "CompanyId", "Id", 'photo.jpg', 'image/jpeg',
        '', 'stored', 0, now(), now()
      FROM "ConversationMessages" WHERE random() < 0.02;
      ANALYZE "ConversationMessages";
      ANALYZE "MessageAttachments";
      ANALYZE "Conversations";
      """
    );
    var one = await db
      .Conversations.Select(x => x.Id)
      .OrderBy(x => x)
      .FirstAsync();

    var cases = new (string Name, SearchMessagesQuery Query, int Hits)[]
    {
      (
        "rare word, all chats",
        new(null, "tarpaulin", null, null, null, null),
        1
      ),
      (
        "no match, all chats",
        new(null, "nothinglikethis", null, null, null, null),
        0
      ),
      (
        "common word, all chats",
        new(null, "status", null, null, null, null),
        50
      ),
      (
        "no match, one chat",
        new(one, "nothinglikethis", null, null, null, null),
        0
      ),
      ("load 1007, all chats", new(null, null, null, null, null, 1007), 50),
    };
    foreach (var (name, query, hits) in cases)
    {
      var capture = new Capture();
      var search = fixture.Connect(capture);
      var handler = new MessageSearchHandler(
        search,
        new InboxScenario.Caller("me"),
        new TestDriverScope()
      );
      var clock = Stopwatch.StartNew();
      var result = await handler.Handle(query, default);
      clock.Stop();
      Assert.True(result.Success, name);
      Assert.Equal(hits, result.Response!.Hits.Count);
      output.WriteLine(
        $"== {name}: {clock.ElapsedMilliseconds} ms, "
          + $"{capture.Commands.Count} statements"
      );
      foreach (var command in capture.Commands)
        output.WriteLine(await ExplainAsync(fixture, command));
    }
  }

  private static async Task<string> ExplainAsync(
    PostgresFixture fixture,
    (string Sql, (string Name, object? Value)[] Parameters) command
  )
  {
    var db = fixture.Connect();
    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
    await connection.OpenAsync();
    await using var explain = connection.CreateCommand();
    explain.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + command.Sql;
    foreach (var (name, value) in command.Parameters)
      explain.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
    var lines = new List<string>();
    await using var reader = await explain.ExecuteReaderAsync();
    while (await reader.ReadAsync())
      lines.Add(reader.GetString(0));
    // The plan's head and its totals; the full text is long.
    return string.Join(
      "\n",
      lines
        .Where(x =>
          x.Contains("Execution Time")
          || x.Contains("Planning Time")
          || !x.StartsWith("  ")
          || x.Contains("Seq Scan")
          || x.TrimStart().StartsWith("Buffers")
          || x.Contains("Index")
        )
        .Take(18)
    );
  }

  private sealed class Capture : DbCommandInterceptor
  {
    public List<(string, (string, object?)[])> Commands { get; } = [];

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken ct = default
    )
    {
      if (command.CommandText.Contains("\"ConversationMessages\""))
        Commands.Add(
          (
            command.CommandText,
            [
              .. command
                .Parameters.Cast<DbParameter>()
                .Select(x => (x.ParameterName, x.Value)),
            ]
          )
        );
      return ValueTask.FromResult(result);
    }
  }
}
