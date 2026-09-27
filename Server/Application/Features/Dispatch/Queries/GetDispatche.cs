using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Services.Deadheads;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public record GetDispatchQuery(
  int Page = 1,
  int PageSize = 20,
  string? Search = null,
  string? Status = null,
  Guid? TruckId = null,
  bool InChosenGroup = false
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
  DeadheadService deadhead,
  IDriverScope scope
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
      var loadNumber = await LoadNumberSearch.NumberAsync(
        dbContext,
        search,
        cancellationToken
      );
      var digits = loadNumber is { } number
        ? LoadNumberSearch.Prefix(number)
        : null;
      query = query.Where(x =>
        digits != null && x.LoadNumber.ToString().StartsWith(digits)
        || x.OrderNumber.Contains(search)
        || x.CustomerName.Contains(search)
        || x.TruckNumber.Contains(search)
        || x.DriverName.Contains(search)
        || request.Status == "completed" && x.TrailerNumber.Contains(search)
      );
    }
    if (request.Status == "completed")
      query = query.Where(CompletedLoads.Filter(dbContext.LoadExecutionLegs));
    else if (!string.IsNullOrWhiteSpace(request.Status))
      query = query.Where(x => x.Status == request.Status);
    if (request.TruckId.HasValue)
      query = query.Where(x =>
        x.PlanningTruckId == request.TruckId
        || x.TruckId == request.TruckId
        || x.Stops.Any(s => s.TruckId == request.TruckId)
      );
    // The dispatcher's chosen driver group: loads one of its drivers drove
    // or drives, on the load or on any of its stops, as recorded.
    if (
      request.InChosenGroup
      && await scope.CurrentAsync(cancellationToken) is { IsAll: false } group
    )
    {
      var drivers = group.Drivers;
      query = query.Where(x =>
        x.DriverId != null && drivers.Contains(x.DriverId.Value)
        || x.Stops.Any(s =>
          s.DriverId != null && drivers.Contains(s.DriverId.Value)
          || s.CoDriverId != null && drivers.Contains(s.CoDriverId.Value)
        )
      );
    }
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
      await CompletedLoads.MarkExecutionAsync(
        dbContext.LoadExecutionLegs,
        items,
        cancellationToken
      );
      await deadhead.ReadCompletedAsync(items, cancellationToken);
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
