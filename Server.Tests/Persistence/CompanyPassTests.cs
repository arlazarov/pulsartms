using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Persistence;

using Load = global::Domain.Entities.Dispatch.Dispatch;

// Background work goes round the carriers one at a time. What matters is
// that each turn sees one carrier's rows and only theirs - including from
// scopes the work opens beneath itself, which is where a choice held in a
// scoped field was lost.
[Trait("Category", "Persistence")]
[Trait("Kind", "Integration")]
public sealed class CompanyPassTests
{
  private static readonly Guid Other = new(
    "b1e1b1e1-0000-4000-8000-000000000002"
  );

  [Fact]
  public async Task APassGoesRoundEveryCarrierAndEachTurnSeesOnlyItsOwnRows()
  {
    await using var connection = new SqliteConnection("DataSource=:memory:");
    await connection.OpenAsync();
    await using var provider = Server(connection);
    await SeedAsync(connection);

    var seen = new List<(Guid? Carrier, string[] Orders)>();
    await using var scope = provider.CreateAsyncScope();
    await CompanyPasses.ForEachCompanyAsync(
      scope.ServiceProvider,
      async ct =>
      {
        // A scope of its own, the way a handler sent through the mediator
        // gets one. The carrier has to reach it.
        await using var nested = provider.CreateAsyncScope();
        var db = nested.ServiceProvider.GetRequiredService<AppDbContext>();
        seen.Add(
          (
            db.ServingCompany,
            await db.Dispatches.Select(x => x.OrderNumber).ToArrayAsync(ct)
          )
        );
      },
      default
    );

    Assert.Equal(2, seen.Count);
    Assert.Contains(
      seen,
      x => x.Carrier == Company.Amf && x.Orders is ["AMF-1"]
    );
    Assert.Contains(seen, x => x.Carrier == Other && x.Orders is ["OTHER-1"]);
    // And once the pass is over, nobody is being served again.
    Assert.Null(provider.GetRequiredService<ICurrentCompany>().Id);
  }

  [Fact]
  public async Task TwoPassesRunningAtOnceDoNotSeeEachOthersCarrier()
  {
    await using var connection = new SqliteConnection("DataSource=:memory:");
    await connection.OpenAsync();
    await using var provider = Server(connection);
    var current = provider.GetRequiredService<ICurrentCompany>();
    var bothInside = new TaskCompletionSource();
    var inside = 0;

    async Task<Guid?> PassAsync(Guid company)
    {
      using var serving = current.As(company);
      if (Interlocked.Increment(ref inside) == 2)
        bothInside.SetResult();
      await bothInside.Task;
      await Task.Yield();
      return current.Id;
    }

    var results = await Task.WhenAll(
      Task.Run(() => PassAsync(Company.Amf)),
      Task.Run(() => PassAsync(Other))
    );

    Assert.Equal([Company.Amf, Other], results);
  }

  private static ServiceProvider Server(SqliteConnection connection)
  {
    var services = new ServiceCollection();
    services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
    services.AddSingleton<ICurrentCompany, CurrentCompany>();
    services.AddScoped<ICompanyRoster, CompanyRoster>();
    services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
    return services.BuildServiceProvider();
  }

  private static async Task SeedAsync(SqliteConnection connection)
  {
    // Built by hand, so it serves the one carrier and explicit owners are
    // kept as written.
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    db.Companies.AddRange(
      new()
      {
        Id = Company.Amf,
        Key = "amfcarrier",
        Name = "AMF Carrier",
      },
      new()
      {
        Id = Other,
        Key = "other",
        Name = "Other Carrier",
      }
    );
    db.Dispatches.AddRange(
      new Load
      {
        Id = Guid.NewGuid(),
        OrderNumber = "AMF-1",
        LoadNumber = 1,
        CompanyId = Company.Amf,
      },
      new Load
      {
        Id = Guid.NewGuid(),
        OrderNumber = "OTHER-1",
        LoadNumber = 1,
        CompanyId = Other,
      }
    );
    await db.SaveChangesAsync();
  }
}
