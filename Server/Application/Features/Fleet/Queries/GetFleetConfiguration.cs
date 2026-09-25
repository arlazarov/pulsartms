using Application.Features.Fleet.Services;
using Application.Models;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Queries;

// Status is the resource's lifecycle, IsActive: the same field Messaging's
// Archive reads for a driver. Active is the default; Inactive and All are
// kept reachable, and nothing is removed or changed by reading them.
public sealed record GetFleetConfigurationQuery(
  string Kind,
  string? Search = null,
  int Page = 1,
  string Status = "active"
) : IRequest<RequestResponse<FleetConfigurationPage>>, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (!FleetConfigurationAccess.ValidKind(Kind))
      yield return "Choose trucks, trailers or drivers.";
    if (Status is not ("active" or "inactive" or "all"))
      yield return "Choose active, inactive or all.";
    if (Search?.Length > 100)
      yield return "That search is too long.";
    if (Page is < 1 or > 10000)
      yield return "Choose a page from 1 to 10000.";
  }
}

// One page of the chosen status, with how many of each status match the
// search, so the page can say what the other choices hold.
public sealed class FleetConfigurationPage : ListResult<FleetConfigurationRow>
{
  public int ActiveCount { get; init; }
  public int InactiveCount { get; init; }
}

public sealed class GetFleetConfigurationHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
)
  : IRequestHandler<
    GetFleetConfigurationQuery,
    RequestResponse<FleetConfigurationPage>
  >
{
  public async Task<RequestResponse<FleetConfigurationPage>> Handle(
    GetFleetConfigurationQuery request,
    CancellationToken ct
  )
  {
    if (!await FleetConfigurationAccess.IsAdminAsync(db, caller, roles, ct))
      return RequestResponse<FleetConfigurationPage>.Fail(
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
    // Both counts in the one read that used to count the page's rows.
    var counts = await query
      .GroupBy(x => x.IsActive)
      .Select(x => new { Active = x.Key, Count = x.Count() })
      .ToListAsync(ct);
    var active = counts.Where(x => x.Active).Sum(x => x.Count);
    var inactive = counts.Where(x => !x.Active).Sum(x => x.Count);
    query = request.Status switch
    {
      "inactive" => query.Where(x => !x.IsActive),
      "all" => query,
      _ => query.Where(x => x.IsActive),
    };
    var rows = await query
      .OrderBy(x => x.Name)
      .ThenBy(x => x.Id)
      .Skip((request.Page - 1) * 30)
      .Take(30)
      .ToListAsync(ct);
    return RequestResponse<FleetConfigurationPage>.Ok(
      new()
      {
        Items = rows,
        TotalCount = request.Status switch
        {
          "inactive" => inactive,
          "all" => active + inactive,
          _ => active,
        },
        ActiveCount = active,
        InactiveCount = inactive,
      }
    );
  }
}
