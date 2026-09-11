using Application.Models;

namespace Application.Features.Fleet.Queries;

public record GetTrucksQuery : IRequest<RequestResponse<ListResult<TruckDto>>>;

public record TruckDto(
  Guid Id,
  string ExternalId,
  string UnitNumber,
  string Vin,
  bool IsActive,
  Guid? DriverId,
  Guid? TrailerId
);

public class GetTrucksHandler(IAppDbContext dbContext)
  : IRequestHandler<GetTrucksQuery, RequestResponse<ListResult<TruckDto>>>
{
  public async Task<RequestResponse<ListResult<TruckDto>>> Handle(
    GetTrucksQuery request,
    CancellationToken cancellationToken
  )
  {
    var items = await dbContext
      .Trucks.AsNoTracking()
      .OrderBy(x => x.UnitNumber)
      .Select(x => new TruckDto(
        x.Id,
        x.ExternalId,
        x.UnitNumber,
        x.Vin,
        x.IsActive,
        x.DriverId,
        x.TrailerId
      ))
      .ToListAsync(cancellationToken);

    return RequestResponse<ListResult<TruckDto>>.Ok(
      new ListResult<TruckDto> { TotalCount = items.Count, Items = items }
    );
  }
}
