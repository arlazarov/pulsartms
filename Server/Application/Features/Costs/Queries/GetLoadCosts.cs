using Application.Features.Costs.Models;
using Application.Features.Mileage.Services;
using Application.Models;

namespace Application.Features.Costs.Queries;

public sealed record GetLoadCostsQuery(Guid DispatchId)
  : IRequest<RequestResponse<LoadCostBreakdown>>;

public sealed class GetLoadCostsHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<GetLoadCostsQuery, RequestResponse<LoadCostBreakdown>>
{
  private const int MaximumRows = 200;

  public async Task<RequestResponse<LoadCostBreakdown>> Handle(
    GetLoadCostsQuery request,
    CancellationToken ct
  )
  {
    if (await MileageAccess.ActorAsync(db, caller, roles, false, ct) is null)
      return RequestResponse<LoadCostBreakdown>.Fail("Access denied.", 403);
    var id = request.DispatchId;
    if (!await db.Dispatches.AsNoTracking().AnyAsync(x => x.Id == id, ct))
      return RequestResponse<LoadCostBreakdown>.Fail("Load not found.", 404);

    var rows = await (
      from attribution in db.ExpenseAttributions.AsNoTracking()
      join expense in db.Expenses.AsNoTracking()
        on attribution.ExpenseId equals expense.Id
      where attribution.DispatchId == id
      orderby expense.OccurredAt descending, expense.Id
      select new LoadCostRow(
        expense.Id,
        expense.Kind,
        expense.OccurredAt,
        expense.Location,
        attribution.Amount,
        expense.Currency,
        expense.Amount,
        attribution.Basis,
        attribution.ManualOverride
      )
    )
      .Take(MaximumRows + 1)
      .ToListAsync(ct);
    var truncated = rows.Count > MaximumRows;
    if (truncated)
      rows.RemoveAt(rows.Count - 1);

    // Totals stay within one currency. A load that bears costs in more than
    // one currency gets one total per currency rather than a converted sum,
    // because the rate and the moment it applied belong to the conversion.
    var totals = rows
      .GroupBy(x => new { x.Currency, x.Kind })
      .Select(x => new LoadCostTotal(
        x.Key.Currency,
        x.Key.Kind,
        x.Sum(row => row.Amount)
      ))
      .OrderBy(x => x.Currency)
      .ThenBy(x => x.Kind)
      .ToList();

    return RequestResponse<LoadCostBreakdown>.Ok(
      new(id, totals, rows, truncated)
    );
  }
}
