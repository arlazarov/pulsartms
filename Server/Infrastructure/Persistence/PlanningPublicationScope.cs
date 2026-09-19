using System.Data;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Infrastructure.Persistence;

public sealed class PlanningPublicationScope(AppDbContext db)
  : IPlanningPublicationScope
{
  public async Task<IDbContextTransaction> BeginAsync(
    Guid? truckId,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    if (db.Database.CurrentTransaction is not null)
      throw new InvalidOperationException(
        "Planning publication requires its own fresh transaction."
      );
    var isolation = db.Database.ProviderName switch
    {
      "Npgsql.EntityFrameworkCore.PostgreSQL" => IsolationLevel.RepeatableRead,
      "Microsoft.EntityFrameworkCore.Sqlite" => IsolationLevel.Serializable,
      _ => throw new NotSupportedException(
        "The database provider cannot protect planning publication."
      ),
    };
    var transaction = await db.Database.BeginTransactionAsync(isolation, ct);
    try
    {
      if (db.Database.IsNpgsql())
      {
        // Writer triggers share the global row before advancing all affected
        // trucks. Global membership/settings changes own that row exclusively.
        var scoped = truckId is { } id && id != Guid.Empty;
        await RequireRevisionAsync(Guid.Empty, !scoped, ct);
        if (scoped)
          await RequireRevisionAsync(truckId!.Value, true, ct);
      }
      return transaction;
    }
    catch (Exception ex)
    {
      await transaction.DisposeAsync();
      if (IsBusy(ex))
        throw new RoutePlanningException(
          "Planning inputs are being updated. Retry planning shortly.",
          DateTime.UtcNow.AddSeconds(5)
        );
      throw;
    }
  }

  private static bool IsBusy(Exception exception)
  {
    for (
      Exception? current = exception;
      current is not null;
      current = current.InnerException
    )
      if (
        current is PostgresException
        {
          SqlState: PostgresErrorCodes.LockNotAvailable
            or PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
        }
      )
        return true;
    return false;
  }

  private async Task RequireRevisionAsync(
    Guid truckId,
    bool exclusive,
    CancellationToken ct
  )
  {
    var sql = exclusive
      ? """
        SELECT "Revision" AS "Value" FROM "PlanningInputRevisions"
        WHERE "TruckId" = @truck FOR UPDATE NOWAIT
        """
      : """
        SELECT "Revision" AS "Value" FROM "PlanningInputRevisions"
        WHERE "TruckId" = @truck FOR SHARE NOWAIT
        """;
    var rows = await db
      .Database.SqlQueryRaw<long>(sql, new NpgsqlParameter("truck", truckId))
      .ToArrayAsync(ct);
    if (rows.Length != 1)
      throw new RoutePlanningException(
        "Planning input ownership changed. Refresh the truck work."
      );
  }
}
