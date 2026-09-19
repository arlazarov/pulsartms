using Application.Features.Costs.Models;
using Application.Features.Costs.Services;
using Application.Features.Mileage.Services;
using Application.Models;
using Domain.Entities.Costs;

namespace Application.Features.Costs.Commands;

// The caller states the complete set of shares for the expense, so replacing
// a share is checked as a whole rather than as a difference and a partial
// write cannot leave the expense over-attributed.
public sealed record SetExpenseAttributionCommand(
  Guid ExpenseId,
  ExpenseAttributionUpdate Update
) : IRequest<RequestResponse<ExpenseAttributionRow>>;

public sealed class SetExpenseAttributionHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    SetExpenseAttributionCommand,
    RequestResponse<ExpenseAttributionRow>
  >
{
  public async Task<RequestResponse<ExpenseAttributionRow>> Handle(
    SetExpenseAttributionCommand command,
    CancellationToken ct
  )
  {
    var actor = await MileageAccess.ActorAsync(db, caller, roles, false, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    var update = command.Update;
    if (update is null || update.Revision < 0)
      return Fail("State the expense revision being corrected.", 400);
    if (update.Shares is null)
      return Fail("State the complete set of shares.", 400);
    if (!ExpenseAttributionRules.ValidBasis(update.Basis))
      return Fail("Choose a recorded attribution basis.", 400);
    var reason = (update.Reason ?? "").Trim();
    if (reason.Length is 0 or > 400)
      return Fail("Enter a reason of at most 400 characters.", 400);

    var expense = await db.Expenses.SingleOrDefaultAsync(
      x => x.Id == command.ExpenseId,
      ct
    );
    if (expense is null)
      return Fail("Expense not found.", 404);
    if (expense.Revision != update.Revision)
      return Conflict();

    var intended = update
      .Shares.Select(x => new AttributionShare(x.DispatchId, x.Amount))
      .ToArray();
    if (ExpenseAttributionRules.Rejection(expense, intended) is { } rejection)
      return Fail(rejection, 400);

    var existing = await db
      .ExpenseAttributions.Where(x => x.ExpenseId == expense.Id)
      .ToListAsync(ct);
    var now = clock.GetUtcNow().UtcDateTime;
    var manual = update.Basis == "manual";
    expense.Revision++;

    foreach (var share in intended)
    {
      var current = existing.SingleOrDefault(x =>
        x.DispatchId == share.DispatchId
      );
      if (current is not null && current.Amount == share.Amount)
        continue;
      db.ExpenseAttributionEvents.Add(
        Event(
          expense,
          current,
          share.DispatchId,
          share.Amount,
          update,
          reason,
          manual,
          actor.Value,
          now
        )
      );
      if (current is null)
        db.ExpenseAttributions.Add(
          new()
          {
            Id = Guid.NewGuid(),
            ExpenseId = expense.Id,
            DispatchId = share.DispatchId,
            Amount = share.Amount,
            Basis = update.Basis,
            Reason = reason,
            ManualOverride = manual,
            Revision = 1,
            RecordedAt = now,
            RecordedBy = actor.Value,
          }
        );
      else
      {
        current.Amount = share.Amount;
        current.Basis = update.Basis;
        current.Reason = reason;
        current.ManualOverride = manual;
        current.Revision++;
        current.RecordedAt = now;
        current.RecordedBy = actor.Value;
      }
    }

    // A share the caller left out is removed, and the removal is recorded as
    // an amount of zero so the history shows what the load stopped bearing.
    foreach (
      var removed in existing.Where(x =>
        intended.All(share => share.DispatchId != x.DispatchId)
      )
    )
    {
      db.ExpenseAttributionEvents.Add(
        Event(
          expense,
          removed,
          removed.DispatchId,
          0m,
          update,
          reason,
          manual,
          actor.Value,
          now
        )
      );
      db.ExpenseAttributions.Remove(removed);
    }

    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return Conflict();
    }

    var saved = await db
      .ExpenseAttributions.AsNoTracking()
      .Where(x => x.ExpenseId == expense.Id)
      .ToListAsync(ct);
    return RequestResponse<ExpenseAttributionRow>.Ok(
      new(
        expense.Id,
        expense.Revision,
        expense.Amount,
        expense.Currency,
        ExpenseAttributionRules.Unattributed(expense, saved),
        [
          .. saved
            .OrderBy(x => x.DispatchId)
            .Select(x => new ExpenseShareRow(
              x.DispatchId,
              x.Amount,
              x.Basis,
              x.Reason,
              x.ManualOverride
            )),
        ]
      )
    );
  }

  private static ExpenseAttributionEvent Event(
    Expense expense,
    ExpenseAttribution? current,
    Guid dispatchId,
    decimal amount,
    ExpenseAttributionUpdate update,
    string reason,
    bool manual,
    Guid actor,
    DateTime now
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      ExpenseId = expense.Id,
      AttributionId = current?.Id,
      DispatchId = dispatchId,
      Revision = expense.Revision,
      PreviousAmount = current?.Amount ?? 0m,
      Amount = amount,
      Basis = update.Basis,
      Reason = reason,
      ManualOverride = manual,
      RecordedAt = now,
      RecordedBy = actor,
    };

  private static RequestResponse<ExpenseAttributionRow> Fail(
    string message,
    int status
  ) => RequestResponse<ExpenseAttributionRow>.Fail(message, status);

  private static RequestResponse<ExpenseAttributionRow> Conflict() =>
    Fail("Expense changed. Reload its attribution before saving.", 409);
}
