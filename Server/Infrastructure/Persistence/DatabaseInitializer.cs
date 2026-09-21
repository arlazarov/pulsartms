using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

public sealed class DatabaseInitializer(
  IServiceScopeFactory scopes,
  IConfiguration configuration,
  ILogger<DatabaseInitializer> logger
) : IHostedService
{
  private readonly CancellationTokenSource stopping = new();
  private Task adoption = Task.CompletedTask;

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    if (!configuration.GetValue<bool>("Database:ApplyMigrations"))
      return;
    await using (var scope = scopes.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      await db.Database.MigrateAsync(cancellationToken);
      await AdoptRowsWrittenDuringTheRolloutAsync(db, cancellationToken);
    }
    // Once at startup is not enough: this revision migrates before it is
    // given any traffic, and the previous one goes on writing until it is
    // drained. So the question is asked again every few minutes. It costs
    // one cheap look per table and changes nothing unless a row is found.
    adoption = Task.Run(() => KeepAdoptingAsync(stopping.Token));
  }

  private async Task KeepAdoptingAsync(CancellationToken ct)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
    try
    {
      while (await timer.WaitForNextTickAsync(ct))
      {
        try
        {
          await using var scope = scopes.CreateAsyncScope();
          await AdoptRowsWrittenDuringTheRolloutAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            ct
          );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          logger.LogWarning(
            ex,
            "Ownerless rows could not be adopted this round"
          );
        }
      }
    }
    catch (OperationCanceledException) { }
  }

  // While a release rolls out, the previous version is still running and
  // still writing, and it has never heard of carriers: its rows arrive
  // with the empty carrier the column defaults to, which the filters then
  // hide from everyone. For as long as there is exactly one carrier there
  // is no doubt whose those rows are, so they are handed to it. With two
  // carriers nothing is guessed - an ownerless row stays hidden until a
  // person decides whose it is.
  private async Task AdoptRowsWrittenDuringTheRolloutAsync(
    AppDbContext db,
    CancellationToken ct
  )
  {
    if (!db.Database.IsNpgsql())
      return;
    var carriers = await db.Companies.Select(x => x.Id).Take(2).ToListAsync(ct);
    if (carriers.Count != 1)
      return;
    foreach (
      var table in db
        .Model.GetEntityTypes()
        .Where(x =>
          x.FindProperty(nameof(Domain.Entities.ICompanyOwned.CompanyId))
            is not null
          && !x.IsOwned()
        )
        .Select(x => x.GetTableName())
        .Where(x => x is not null)
        .Distinct()
    )
    {
      var ownerless = await db
        .Database.SqlQueryRaw<bool>(
          $$"""
          SELECT EXISTS (
            SELECT 1 FROM "{{table}}" WHERE "CompanyId" = {0}
          ) AS "Value"
          """,
          Guid.Empty
        )
        .SingleAsync(ct);
      if (!ownerless)
        continue;
      logger.LogWarning(
        "Adopting rows in {Table} written without a carrier",
        table
      );
      // Accepted history is held immutable by a trigger. Saying whose a
      // revision is changes nothing that was accepted, so the trigger
      // stands aside for that one statement and is put straight back.
      var guarded = table == "ExecutionLegRevisions";
      await using var transaction = await db.Database.BeginTransactionAsync(ct);
      if (guarded)
        await db.Database.ExecuteSqlRawAsync(
          """ALTER TABLE "ExecutionLegRevisions" DISABLE TRIGGER execution_history_immutable""",
          ct
        );
      await db.Database.ExecuteSqlRawAsync(
        $$"""UPDATE "{{table}}" SET "CompanyId" = {0} WHERE "CompanyId" = {1}""",
        [carriers[0], Guid.Empty],
        ct
      );
      if (guarded)
        await db.Database.ExecuteSqlRawAsync(
          """ALTER TABLE "ExecutionLegRevisions" ENABLE TRIGGER execution_history_immutable""",
          ct
        );
      await transaction.CommitAsync(ct);
    }
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    await stopping.CancelAsync();
    await adoption;
  }
}
