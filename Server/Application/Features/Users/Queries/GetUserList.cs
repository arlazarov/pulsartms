using Application.Features.Users.Models;
using Application.Models;

namespace Application.Features.Users.Queries;

public class GetUserListQuery
  : ListQuery,
    IRequest<RequestResponse<PaginatedList<UserDto>>> { }

public class GetUserListValidator : AbstractValidator<GetUserListQuery>
{
  public GetUserListValidator()
  {
    RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
    RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    RuleFor(x => x.Search).MaximumLength(200);
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
