using Application.Features.Costs.Models;
using Application.Features.Costs.Services;
using Application.Features.Mileage.Services;
using Application.Models;
using Domain.Entities.Costs;

namespace Application.Features.Costs.Commands;

public sealed record RecordExpenseCommand(ExpenseEntry Entry)
  : IRequest<RequestResponse<ExpenseAttributionRow>>;

public sealed class RecordExpenseHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    RecordExpenseCommand,
    RequestResponse<ExpenseAttributionRow>
  >
{
  public async Task<RequestResponse<ExpenseAttributionRow>> Handle(
    RecordExpenseCommand command,
    CancellationToken ct
  )
  {
    var actor = await MileageAccess.ActorAsync(db, caller, roles, false, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    var entry = command.Entry;
    if (entry is null)
      return Fail("State the expense.", 400);
    if (!ExpenseAttributionRules.ValidKind(entry.Kind))
      return Fail("Choose a recorded expense kind.", 400);
    if (entry.Amount <= 0)
      return Fail("An expense needs an amount above zero.", 400);
    var currency = (entry.Currency ?? "").Trim().ToUpperInvariant();
    if (currency.Length is < 3 or > 8)
      return Fail("State the currency the expense was paid in.", 400);
    if (entry.OccurredAt == default)
      return Fail("State when the expense occurred.", 400);
    if (entry.Quantity is <= 0)
      return Fail("A stated quantity must be above zero.", 400);

    // An import supplies its own key so a replay is one expense; an expense
    // entered by hand has none and several of them may exist.
    if (
      entry.IdempotencyKey is { } key
      && key != Guid.Empty
      && await db.Expenses.AnyAsync(x => x.IdempotencyKey == key, ct)
    )
      return Fail("This expense was already recorded.", 409);

    var now = clock.GetUtcNow().UtcDateTime;
    var expense = new Expense
    {
      Id = Guid.NewGuid(),
      IdempotencyKey =
        entry.IdempotencyKey == Guid.Empty ? null : entry.IdempotencyKey,
      Kind = entry.Kind,
      OccurredAt = entry.OccurredAt,
      Location = Trim(entry.Location, 500),
      Amount = entry.Amount,
      Currency = currency,
      Quantity = entry.Quantity,
      QuantityUnit = Trim(entry.QuantityUnit, 16),
      TruckId = entry.TruckId,
      DriverId = entry.DriverId,
      TrailerId = entry.TrailerId,
      ExecutionLegId = entry.ExecutionLegId,
      SourceTruckName = Trim(entry.SourceTruckName, 100),
      SourceDriverName = Trim(entry.SourceDriverName, 200),
      Source = Trim(entry.Source, 64),
      SourceReference = Trim(entry.SourceReference, 300),
      RecordedAt = now,
      RecordedBy = actor.Value,
    };
    db.Expenses.Add(expense);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return Fail("This expense was already recorded.", 409);
    }

    return RequestResponse<ExpenseAttributionRow>.Ok(
      new(
        expense.Id,
        expense.Revision,
        expense.Amount,
        expense.Currency,
        expense.Amount,
        []
      )
    );
  }

  private static string Trim(string? value, int length)
  {
    var text = (value ?? "").Trim();
    return text.Length > length ? text[..length] : text;
  }

  private static RequestResponse<ExpenseAttributionRow> Fail(
    string message,
    int status
  ) => RequestResponse<ExpenseAttributionRow>.Fail(message, status);
}
