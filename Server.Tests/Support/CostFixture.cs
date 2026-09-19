using Application.Features.Costs.Commands;
using Application.Features.Costs.Models;
using Application.Features.Costs.Queries;
using Application.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Costs;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Support;

// One truck, two loads and one 400 USD fuel expense: the smallest shape in
// which a single purchase can be divided between loads for different brokers.
internal sealed class CostFixture : IAsyncDisposable
{
  private SqliteConnection Connection { get; init; } = null!;
  public required AppDbContext Db { get; init; }
  public required Guid ExpenseId { get; init; }
  public required Guid First { get; init; }
  public required Guid Second { get; init; }
  public required Guid Actor { get; init; }
  public required SetExpenseAttributionHandler Attribution { get; init; }
  public required GetLoadCostsHandler Costs { get; init; }

  public Task<RequestResponse<ExpenseAttributionRow>> AttributeAsync(
    ExpenseShareUpdate[] shares,
    long revision,
    Guid? expenseId = null
  ) =>
    Attribution.Handle(
      new(
        expenseId ?? ExpenseId,
        new(revision, "manual", "Split by agreement", shares)
      ),
      default
    );

  public Task<RequestResponse<LoadCostBreakdown>> CostsAsync(Guid dispatchId) =>
    Costs.Handle(new(dispatchId), default);

  public async Task<Guid> AddExpenseAsync(
    string kind,
    decimal amount,
    string currency
  )
  {
    var expense = Expense(kind, amount, currency, Actor);
    Db.Expenses.Add(expense);
    await Db.SaveChangesAsync();
    return expense.Id;
  }

  public static async Task<CostFixture> CreateAsync()
  {
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var actor = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "cost-operator",
      IsActive = true,
    };
    db.Users.Add(actor);
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "costs",
      UnitNumber = "1",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    var loads = new[] { Load(truck, 1), Load(truck, 2) };
    db.AddRange(loads);
    var expense = Expense("fuel", 400m, "USD", actor.Id);
    db.Expenses.Add(expense);
    await db.SaveChangesAsync();
    var caller = new Caller();
    var roles = new Roles();
    return new CostFixture
    {
      Connection = connection,
      Db = db,
      ExpenseId = expense.Id,
      First = loads[0].Id,
      Second = loads[1].Id,
      Actor = actor.Id,
      Attribution = new(db, caller, roles, TimeProvider.System),
      Costs = new(db, caller, roles),
    };
  }

  private static Expense Expense(
    string kind,
    decimal amount,
    string currency,
    Guid actor
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      Kind = kind,
      Amount = amount,
      Currency = currency,
      Location = "Buffalo, NY",
      OccurredAt = DateTime.UtcNow,
      RecordedAt = DateTime.UtcNow,
      RecordedBy = actor,
    };

  private static DispatchEntity Load(Truck truck, int number) =>
    new()
    {
      Id = Guid.NewGuid(),
      Truck = truck,
      LoadNumber = number,
      Status = "assigned",
      ShipDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

  public async ValueTask DisposeAsync()
  {
    await Db.DisposeAsync();
    await Connection.DisposeAsync();
  }

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => "cost-operator";
  }

  private sealed class Roles : IUserRoleService
  {
    public Task<string?> GetAsync(string identityId, CancellationToken ct) =>
      Task.FromResult<string?>("Dispatch");

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
