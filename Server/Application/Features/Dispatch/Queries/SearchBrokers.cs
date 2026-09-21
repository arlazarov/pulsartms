using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Models;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Queries;

public sealed record SearchBrokersQuery(string Search)
  : IRequest<RequestResponse<List<BrokerProfile>>>;

public sealed class SearchBrokersHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<SearchBrokersQuery, RequestResponse<List<BrokerProfile>>>
{
  public async Task<RequestResponse<List<BrokerProfile>>> Handle(
    SearchBrokersQuery query,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<List<BrokerProfile>>.Fail("Access denied.", 403);
    if (query.Search is null || query.Search.Length > 200)
      return RequestResponse<List<BrokerProfile>>.Fail(
        "Search is too long.",
        400
      );
    var key = CustomerMatcher.Normalize(query.Search);
    if (key.Length < 2)
      return RequestResponse<List<BrokerProfile>>.Ok([]);
    var rows = await db
      .Customers.AsNoTracking()
      .Where(x => x.NormalizedName.Contains(key))
      .OrderByDescending(x => x.NormalizedName == key)
      .ThenBy(x => x.Name)
      .Take(20)
      .ToListAsync(ct);
    return RequestResponse<List<BrokerProfile>>.Ok(
      rows.Select(BrokerProfiles.Read).ToList()
    );
  }
}
