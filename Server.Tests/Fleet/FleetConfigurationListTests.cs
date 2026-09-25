using Application.Features.Fleet.Queries;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fleet;

// Fleet resources are listed by their lifecycle, IsActive: active by
// default, inactive and all on request, filtered, counted and paged by the
// server. Reading a status changes nothing.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class FleetConfigurationListTests
{
  [Fact]
  public async Task EachStatusIsFilteredCountedAndPagedOnTheServer()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    db.Users.Add(
      new User
      {
        Id = Guid.NewGuid(),
        IdentityUserId = "fleet-admin",
        Name = "Fleet admin",
        Email = "fleet@example.invalid",
      }
    );
    // 31 active trucks fill more than one page; three are inactive.
    for (var i = 0; i < 31; i++)
      db.Trucks.Add(Truck($"A{i:00}", active: true));
    db.Trucks.AddRange(
      Truck("Z-OLD-1", active: false),
      Truck("Z-OLD-2", active: false),
      Truck("A-OLD", active: false)
    );
    await db.SaveChangesAsync();
    var before = await Snapshot(db);
    var handler = new GetFleetConfigurationHandler(
      db,
      new Caller(),
      new Roles()
    );
    async Task<FleetConfigurationPage> Read(
      string status,
      string? search = null,
      int page = 1
    ) =>
      (
        await handler.Handle(
          new GetFleetConfigurationQuery("trucks", search, page, status),
          default
        )
      ).Response!;

    var active = await Read("active");
    Assert.Equal((31, 31, 3), Counts(active));
    Assert.Equal(30, active.Items.Count);
    Assert.All(active.Items, x => Assert.True(x.IsActive));
    Assert.Single((await Read("active", page: 2)).Items);

    var inactive = await Read("inactive");
    Assert.Equal((3, 31, 3), Counts(inactive));
    Assert.Equal(
      ["A-OLD", "Z-OLD-1", "Z-OLD-2"],
      inactive.Items.Select(x => x.Name)
    );

    var all = await Read("all");
    Assert.Equal((34, 31, 3), Counts(all));

    // Search narrows every count alike.
    var searched = await Read("inactive", "old-1");
    Assert.Equal((1, 0, 1), Counts(searched));
    Assert.Equal("Z-OLD-1", Assert.Single(searched.Items).Name);

    // Nothing was changed by reading, and an unknown status is refused.
    Assert.Equal(before, await Snapshot(db));
    Assert.Contains(
      "Choose active, inactive or all.",
      new GetFleetConfigurationQuery("trucks", Status: "archived").Wrong()
    );
  }

  private static (int Total, int Active, int Inactive) Counts(
    FleetConfigurationPage page
  ) => (page.TotalCount, page.ActiveCount, page.InactiveCount);

  private static Truck Truck(string unit, bool active) =>
    new()
    {
      Id = Guid.NewGuid(),
      UnitNumber = unit,
      ExternalId = unit,
      IsActive = active,
    };

  private static async Task<string> Snapshot(AppDbContext db) =>
    string.Join(
      ";",
      (
        await db.Trucks.AsNoTracking().OrderBy(x => x.UnitNumber).ToListAsync()
      ).Select(x => $"{x.UnitNumber}:{x.IsActive}:{x.ConfigurationRevision}")
    );

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => "fleet-admin";
  }

  private sealed class Roles : IUserRoleService
  {
    public Task<string?> GetAsync(string identityId, CancellationToken ct) =>
      Task.FromResult<string?>("Admin");

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string identityId,
      string role,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }
}
