using Application.Features.Dispatch.Activity;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Support;

internal sealed class DispatchActivityFixture : IAsyncDisposable
{
  private readonly SqliteConnection _connection = new("Data Source=:memory:");
  public AppDbContext Db { get; private set; } = null!;
  public ManualTimeProvider Clock { get; } = new();
  public User Actor { get; } =
    new()
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "journal-operator",
      Name = "Journal operator",
      Email = "journal@example.invalid",
    };
  public DispatchEntity Load { get; } =
    new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1001,
      Status = "in_transit",
      Stops =
      [
        new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Name = "Repeated facility",
          City = "Fixture",
          Job = "Pick Up",
        },
      ],
    };

  public AddDispatchActivityHandler Add(string? role = "Dispatch") =>
    new(Db, new Caller(), new Roles(role), Clock);

  public ResolveDispatchActivityHandler Resolve(string? role = "Dispatch") =>
    new(Db, new Caller(), new Roles(role), Clock);

  public GetDispatchActivityHandler Read(string? role = "Dispatch") =>
    new(Db, new Caller(), new Roles(role));

  public AddDispatchActivityCommand Command(
    long revision = 0,
    bool attention = false
  ) =>
    new(
      Load.Id,
      new(
        Guid.NewGuid(),
        revision,
        "driver-called",
        "Driver reported a delay.",
        Load.Stops[0].Id,
        null,
        attention
      )
    );

  public static async Task<DispatchActivityFixture> CreateAsync()
  {
    var fixture = new DispatchActivityFixture();
    await fixture._connection.OpenAsync();
    fixture.Db = new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(fixture._connection)
        .Options
    );
    await fixture.Db.Database.EnsureCreatedAsync();
    fixture.Db.Users.Add(fixture.Actor);
    fixture.Db.Dispatches.Add(fixture.Load);
    await fixture.Db.SaveChangesAsync();
    return fixture;
  }

  public async ValueTask DisposeAsync()
  {
    await Db.DisposeAsync();
    await _connection.DisposeAsync();
  }

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => "journal-operator";
  }

  private sealed class Roles(string? role) : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string role,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }
}
