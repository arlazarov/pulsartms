using Application.Features.Users.Models;
using Application.Models;

namespace Application.Features.Users.Queries;

public class GetUserListQuery
  : ListQuery,
    IRequest<RequestResponse<PaginatedList<UserDto>>>,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (Page is < 1 or > 1000000)
      yield return "Choose a page from 1 to 1000000.";
    if (PageSize is < 1 or > 100)
      yield return "Ask for between 1 and 100 rows at a time.";
    if (Search?.Length > 200)
      yield return "That search is too long.";
  }
}

public class GetUserListHandler(IAppDbContext dbContext, IUserRoleService roles)
  : IRequestHandler<GetUserListQuery, RequestResponse<PaginatedList<UserDto>>>
{
  public async Task<RequestResponse<PaginatedList<UserDto>>> Handle(
    GetUserListQuery request,
    CancellationToken cancellationToken
  )
  {
    var query = dbContext.Users.AsQueryable();

    if (!string.IsNullOrWhiteSpace(request.Search))
    {
      query = query.Where(x =>
        x.Name.Contains(request.Search) || x.Email.Contains(request.Search)
      );
    }

    var totalCount = await query.CountAsync(cancellationToken);

    var items = await query
      .OrderBy(x => x.Name)
      .ThenBy(x => x.Id)
      .Skip((request.Page - 1) * request.PageSize)
      .Take(request.PageSize)
      .Select(x => new UserDto(x.Id, x.Name, x.Email, x.IsActive))
      .ToListAsync(cancellationToken);

    var assigned = await roles.GetAsync(
      items.Select(x => x.Id).ToArray(),
      cancellationToken
    );
    items = items.Select(x => x with { Role = assigned[x.Id] }).ToList();
    var result = new PaginatedList<UserDto>
    {
      Items = items,
      Page = request.Page,
      PageSize = request.PageSize,
      TotalCount = totalCount,
    };

    return RequestResponse<PaginatedList<UserDto>>.Ok(result);
  }
}
