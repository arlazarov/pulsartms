using Application.Features.Dispatch.Models;
using Application.Features.Eta.Services;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public record GetDispatchByIdQuery(Guid Id)
  : IRequest<RequestResponse<DispatchResponse>>;

public class GetDispatchByIdHandler(IAppDbContext db, EtaForecastService eta)
  : IRequestHandler<GetDispatchByIdQuery, RequestResponse<DispatchResponse>>
{
  public async Task<RequestResponse<DispatchResponse>> Handle(
    GetDispatchByIdQuery request,
    CancellationToken cancellationToken
  )
  {
    var item = await db
      .Dispatches.AsNoTracking()
      .Where(x => x.Id == request.Id)
      .Select(DispatchProjection.Details)
      .SingleOrDefaultAsync(cancellationToken);
    if (item is not null)
      await eta.PopulateAsync(
        [DispatchProjection.Complete(item)],
        cancellationToken
      );
    return item is null
      ? RequestResponse<DispatchResponse>.Fail("Dispatch not found.", 404)
      : RequestResponse<DispatchResponse>.Ok(item);
  }
}
