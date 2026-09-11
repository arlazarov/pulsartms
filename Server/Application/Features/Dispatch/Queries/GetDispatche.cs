using Application.Features.Dispatch.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public record GetDispatchQuery(int Page = 1, int PageSize = 20, string? Search = null,
  string? Status = null, Guid? TruckId = null) : IRequest<RequestResponse<PaginatedList<DispatchResponse>>>;

public class GetDispatchValidator : AbstractValidator<GetDispatchQuery>
{
  public GetDispatchValidator()
  {
    RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
    RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    RuleFor(x => x.Search).MaximumLength(200);
    RuleFor(x => x.Status).MaximumLength(50);
  }
}

public class GetDispatchQueryHandler(IAppDbContext dbContext, DeadheadService deadhead)
  : IRequestHandler<GetDispatchQuery, RequestResponse<PaginatedList<DispatchResponse>>>
{
  public async Task<RequestResponse<PaginatedList<DispatchResponse>>> Handle(
    GetDispatchQuery request, CancellationToken cancellationToken)
  {
    var query = dbContext.Dispatches.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(request.Search))
    {
      var search = request.Search.Trim();
      int.TryParse(search, out var loadNumber);
      query = query.Where(x => x.LoadNumber == loadNumber || x.OrderNumber.Contains(search)
        || x.CustomerName.Contains(search) || x.TruckNumber.Contains(search)
        || x.DriverName.Contains(search) || request.Status == "completed" && x.TrailerNumber.Contains(search));
    }
    if (!string.IsNullOrWhiteSpace(request.Status)) query = query.Where(x => x.Status == request.Status);
    if (request.TruckId.HasValue) query = query.Where(x => x.TruckId == request.TruckId
      || x.Stops.Any(s => s.TruckId == request.TruckId));
    var count = await query.CountAsync(cancellationToken);
    var page = query.OrderByDescending(x => x.LoadNumber).ThenBy(x => x.Id)
      .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize);
    var completed = request.Status == "completed";
    var items = await (completed ? page.Select(DispatchProjection.Details) : page.Select(x => new DispatchResponse
      {
        Id = x.Id, TruckId = x.TruckId, LoadNumber = x.LoadNumber, OrderNumber = x.OrderNumber,
        Status = x.Status, CustomerName = x.CustomerName, DriverName = x.DriverName,
        TruckNumber = x.TruckNumber, TrailerNumber = x.TrailerNumber,
        ShipDate = x.ShipDate, DeliveryDate = x.DeliveryDate, Price = x.Price,
        Currency = x.Currency, LastSyncedAt = x.LastSyncedAt,
      })).ToListAsync(cancellationToken);
    if (completed) await deadhead.ReadAsync(items, cancellationToken);
    return RequestResponse<PaginatedList<DispatchResponse>>.Ok(new()
    { Items = items, Page = request.Page, PageSize = request.PageSize, TotalCount = count });
  }
}
