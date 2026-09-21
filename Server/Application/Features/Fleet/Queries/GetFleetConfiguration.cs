using Application.Features.Fleet.Services;
using Application.Models;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Queries;

public sealed record GetFleetConfigurationQuery(
  string Kind,
  string? Search = null,
  int Page = 1
) : IRequest<RequestResponse<ListResult<FleetConfigurationRow>>>;

public sealed class GetFleetConfigurationValidator
  : AbstractValidator<GetFleetConfigurationQuery>
{
  public GetFleetConfigurationValidator()
  {
    RuleFor(x => x.Kind).Must(FleetConfigurationAccess.ValidKind);
    RuleFor(x => x.Search).MaximumLength(100);
    RuleFor(x => x.Page).InclusiveBetween(1, 10000);
  }
}

public sealed class GetFleetConfigurationHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
)
  : IRequestHandler<
    GetFleetConfigurationQuery,
    RequestResponse<ListResult<FleetConfigurationRow>>
  >
{
  public async Task<RequestResponse<ListResult<FleetConfigurationRow>>> Handle(
    GetFleetConfigurationQuery request,
    CancellationToken ct
  )
  {
    if (!await FleetConfigurationAccess.IsAdminAsync(db, caller, roles, ct))
      return RequestResponse<ListResult<FleetConfigurationRow>>.Fail(
        "Administrator access is required.",
        403
      );
    var query = request.Kind switch
    {
      "trucks" => db
        .Trucks.AsNoTracking()
        .Select(x => new FleetConfigurationRow
        {
          Id = x.Id,
          Name = x.UnitNumber,
          Vin = x.Vin,
          IsActive = x.IsActive,
          IsLocallyConfigured = x.IsLocallyConfigured,
          Revision = x.ConfigurationRevision,
        }),
      "trailers" => db
        .Trailers.AsNoTracking()
        .Select(x => new FleetConfigurationRow
        {
          Id = x.Id,
          Name = x.UnitNumber,
          Vin = x.Vin,
          IsActive = x.IsActive,
          IsLocallyConfigured = x.IsLocallyConfigured,
          Revision = x.ConfigurationRevision,
        }),
      _ => db
        .Drivers.AsNoTracking()
        .Select(x => new FleetConfigurationRow
        {
          Id = x.Id,
          Name = x.Name,
          Vin = "",
          IsActive = x.IsActive,
          IsLocallyConfigured = x.IsLocallyConfigured,
          Revision = x.ConfigurationRevision,
        }),
    };
    var search = request.Search?.Trim().ToLowerInvariant();
    if (!string.IsNullOrEmpty(search))
      query = query.Where(x =>
        x.Name.ToLower().Contains(search) || x.Vin.ToLower().Contains(search)
      );
    var count = await query.CountAsync(ct);
    var rows = await query
      .OrderBy(x => x.Name)
      .ThenBy(x => x.Id)
      .Skip((request.Page - 1) * 30)
      .Take(30)
      .ToListAsync(ct);
    return RequestResponse<ListResult<FleetConfigurationRow>>.Ok(
      new() { Items = rows, TotalCount = count }
    );
  }
}
