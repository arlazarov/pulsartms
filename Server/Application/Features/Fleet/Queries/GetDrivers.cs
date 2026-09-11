using Application.Models;

namespace Application.Features.Fleet.Queries;

public record GetDriversQuery : IRequest<RequestResponse<ListResult<DriverDto>>>;

public record DriverDto(Guid Id, string ExternalId, string Name, string FuelCard, bool IsActive);

public class GetDriversHandler(IAppDbContext dbContext)
  : IRequestHandler<GetDriversQuery, RequestResponse<ListResult<DriverDto>>>
{
  public async Task<RequestResponse<ListResult<DriverDto>>> Handle(
    GetDriversQuery request,
    CancellationToken cancellationToken
  )
  {
    var items = await dbContext
      .Drivers.AsNoTracking()
      .OrderBy(x => x.Name)
      .Select(x => new DriverDto(x.Id, x.ExternalId, x.Name, x.FuelCard, x.IsActive))
      .ToListAsync(cancellationToken);

    return RequestResponse<ListResult<DriverDto>>.Ok(
      new ListResult<DriverDto> { TotalCount = items.Count, Items = items }
    );
  }
}
