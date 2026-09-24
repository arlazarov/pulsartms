using Application.Features.Messaging.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

// An upsert that keeps the higher revision: there is no read before the
// write for another writer to slip between, and no conflict to retry.
// SQLite, used by tests and local runs, stores GUIDs as upper-case text.
public sealed class ConversationReadMarkers(AppDbContext db)
  : IConversationReadMarkers
{
  public Task AdvanceAsync(
    Guid company,
    Guid conversation,
    Guid user,
    long revision,
    CancellationToken ct
  ) =>
    db.Database.IsNpgsql()
      ? db.Database.ExecuteSqlInterpolatedAsync(
        $"""
        INSERT INTO "ConversationReads"
          ("Id", "CompanyId", "ConversationId", "UserId", "ReadRevision")
        VALUES ({Guid.NewGuid()}, {company}, {conversation}, {user},
          {revision})
        ON CONFLICT ("CompanyId", "ConversationId", "UserId")
        DO UPDATE SET "ReadRevision" = GREATEST(
          "ConversationReads"."ReadRevision", EXCLUDED."ReadRevision")
        """,
        ct
      )
      : db.Database.ExecuteSqlInterpolatedAsync(
        $"""
        INSERT INTO "ConversationReads"
          ("Id", "CompanyId", "ConversationId", "UserId", "ReadRevision")
        VALUES ({Text(Guid.NewGuid())}, {Text(company)},
          {Text(conversation)}, {Text(user)}, {revision})
        ON CONFLICT ("CompanyId", "ConversationId", "UserId")
        DO UPDATE SET "ReadRevision" = max(
          "ConversationReads"."ReadRevision", excluded."ReadRevision")
        """,
        ct
      );

  private static string Text(Guid id) => id.ToString().ToUpperInvariant();
}
