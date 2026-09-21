using Application.Features.Dispatch.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public record GetDispatchQuery(
  int Page = 1,
  int PageSize = 20,
  string? Search = null,
  string? Status = null,
  Guid? TruckId = null
) : IRequest<RequestResponse<PaginatedList<DispatchResponse>>>, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (Page is < 1 or > 1000000)
      yield return "Choose a page from 1 to 1000000.";
    if (PageSize is < 1 or > 100)
      yield return "Ask for between 1 and 100 rows at a time.";
    if (Search?.Length > 200)
      yield return "That search is too long.";
    if (Status?.Length > 50)
      yield return "That status is too long.";
  }
}

public class GetDispatchQueryHandler(
  IAppDbContext dbContext,
  DeadheadService deadhead
)
  : IRequestHandler<
    GetDispatchQuery,
    RequestResponse<PaginatedList<DispatchResponse>>
  >
{
  public async Task<RequestResponse<PaginatedList<DispatchResponse>>> Handle(
    GetDispatchQuery request,
    CancellationToken cancellationToken
  )
  {
    var query = dbContext.Dispatches.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(request.Search))
    {
      var search = request.Search.Trim();
      int.TryParse(search, out var loadNumber);
      query = query.Where(x =>
        x.LoadNumber == loadNumber
        || x.OrderNumber.Contains(search)
        || x.CustomerName.Contains(search)
        || x.TruckNumber.Contains(search)
        || x.DriverName.Contains(search)
        || request.Status == "completed" && x.TrailerNumber.Contains(search)
      );
    }
    if (request.Status == "completed")
      query = query.Where(x =>
        x.Status == "completed"
        || x.Stops.Any(s =>
          s.ManualCompletedAt != null || s.CompletionOverride == true
        )
          && x.Stops.All(s =>
            s.CompletionOverride == true
            || s.CompletionOverride != false
              && (
                s.ManualCompletedAt != null
                || s.DepartedAt != null
                || s.DeliveredAt != null
                || s.PickedUpAt != null
              )
            || s.Sequence
              < (
                x.PlanningFromStopId != null
                  ? x
                    .Stops.Where(p => p.Id == x.PlanningFromStopId)
                    .Select(p => (int?)p.Sequence)
                    .FirstOrDefault()
                  : x
                    .Stops.Where(p => p.TruckId != null || p.TruckNumber != "")
                    .OrderBy(p => p.Sequence)
                    .Select(p => (int?)p.Sequence)
                    .FirstOrDefault()
              )
          )
      );
    else if (!string.IsNullOrWhiteSpace(request.Status))
      query = query.Where(x => x.Status == request.Status);
    if (request.TruckId.HasValue)
      query = query.Where(x =>
        x.PlanningTruckId == request.TruckId
        || x.TruckId == request.TruckId
        || x.Stops.Any(s => s.TruckId == request.TruckId)
      );
    var count = await query.CountAsync(cancellationToken);
    var page = query
      .OrderByDescending(x => x.LoadNumber)
      .ThenBy(x => x.Id)
      .Skip((request.Page - 1) * request.PageSize)
      .Take(request.PageSize);
    var completed = request.Status == "completed";
    var items = await (
      completed
        ? page.Select(DispatchProjection.Details)
        : page.Select(x => new DispatchResponse
        {
          Id = x.Id,
          TruckId = x.PlanningTruckId ?? x.TruckId,
          LoadNumber = x.LoadNumber,
          OrderNumber = x.OrderNumber,
          Status = x.Status,
          CustomerName = x.CustomerName,
          DriverName = x.DriverName,
          TruckNumber =
            x.PlanningTruck != null
              ? x.PlanningTruck.UnitNumber
              : x.TruckNumber,
          TrailerNumber = x.TrailerNumber,
          ShipDate = x.ShipDate,
          DeliveryDate = x.DeliveryDate,
          Price = x.Price,
          Currency = x.Currency,
          LastSyncedAt = x.LastSyncedAt,
        })
    ).ToListAsync(cancellationToken);
    if (completed)
    {
      foreach (var load in items)
        DispatchProjection.Complete(load);
      await deadhead.ReadAsync(items, cancellationToken);
    }
    return RequestResponse<PaginatedList<DispatchResponse>>.Ok(
      new()
      {
        Items = items,
        Page = request.Page,
        PageSize = request.PageSize,
        TotalCount = count,
      }
    );
  }
}
