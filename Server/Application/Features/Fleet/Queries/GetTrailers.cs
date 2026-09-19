using Application.Models;

namespace Application.Features.Fleet.Queries;

public record GetTrailersQuery
  : IRequest<RequestResponse<ListResult<TrailerDto>>>;

public record TrailerDto(
  Guid Id,
  string ExternalId,
  string UnitNumber,
  string Vin,
  bool IsActive
);

public class GetTrailersHandler(IAppDbContext dbContext)
  : IRequestHandler<GetTrailersQuery, RequestResponse<ListResult<TrailerDto>>>
{
  public async Task<RequestResponse<ListResult<TrailerDto>>> Handle(
    GetTrailersQuery request,
    CancellationToken cancellationToken
  )
  {
    var items = await dbContext
      .Trailers.AsNoTracking()
      .OrderBy(x => x.UnitNumber)
      .Select(x => new TrailerDto(
        x.Id,
        x.ExternalId,
        x.UnitNumber,
        x.Vin,
        x.IsActive
      ))
      .ToListAsync(cancellationToken);

    return RequestResponse<ListResult<TrailerDto>>.Ok(
      new ListResult<TrailerDto> { TotalCount = items.Count, Items = items }
    );
  }
}
