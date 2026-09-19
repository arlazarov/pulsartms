using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Models;

namespace Application.Features.Fleet.Queries;

public sealed record GetFleetHosQuery(Guid[]? TruckIds = null)
  : IRequest<RequestResponse<Dictionary<Guid, TruckHosSnapshot>>>;

public sealed class GetFleetHosValidator : AbstractValidator<GetFleetHosQuery>
{
  public GetFleetHosValidator()
  {
    RuleFor(x => x.TruckIds)
      .Must(ids =>
        ids is null || ids.Length <= 100 && ids.All(id => id != Guid.Empty)
      );
  }
}

public sealed class GetFleetHosHandler(
  IAppDbContext db,
  IDriverHosProvider hos,
  IDriverHosStore store
)
  : IRequestHandler<
    GetFleetHosQuery,
    RequestResponse<Dictionary<Guid, TruckHosSnapshot>>
  >
{
  public async Task<RequestResponse<Dictionary<Guid, TruckHosSnapshot>>> Handle(
    GetFleetHosQuery request,
    CancellationToken ct
  )
  {
    // Demand the shared snapshot before reading assignments; never wait for a
    // route or call Samsara per viewer. An instance that does not run the
    // refresh, or one that has just restarted, holds nothing in memory and
    // reads what the last refresh recorded instead of reporting no hours.
    var clocks = await hos.GetClocksAsync(ct);
    if (clocks.Count == 0)
      clocks = await store.ReadAsync(ct);
    var trucks = db.Trucks.AsNoTracking();
    trucks = request.TruckIds is { } ids
      ? trucks.Where(x => ids.Contains(x.Id))
      : trucks.Where(x => x.IsActive);
    var drivers = await trucks
      .Select(x => new
      {
        x.Id,
        Name = x.Driver == null ? "" : x.Driver.Name,
        ExternalId = x.Driver == null ? "" : x.Driver.ExternalId,
      })
      .ToArrayAsync(ct);
    return RequestResponse<Dictionary<Guid, TruckHosSnapshot>>.Ok(
      drivers.ToDictionary(
        x => x.Id,
        x => new TruckHosSnapshot(
          x.Name,
          string.IsNullOrEmpty(x.ExternalId)
            ? null
            : clocks.GetValueOrDefault(x.ExternalId)
        )
      )
    );
  }
}
