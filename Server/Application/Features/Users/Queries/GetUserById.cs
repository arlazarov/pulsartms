using Application.Features.Users.Models;
using Application.Models;

namespace Application.Features.Users.Queries;

public record GetUserByIdQuery(Guid Id) : IRequest<RequestResponse<UserDto>>;

public class GetUserByIdHandler(IAppDbContext dbContext, IUserRoleService roles)
  : IRequestHandler<GetUserByIdQuery, RequestResponse<UserDto>>
{
  public async Task<RequestResponse<UserDto>> Handle(
    GetUserByIdQuery request,
    CancellationToken cancellationToken
  )
  {
    var user = await dbContext
      .Users.Where(x => x.Id == request.Id)
      .Select(x => new UserDto(x.Id, x.Name, x.Email, x.IsActive))
      .FirstOrDefaultAsync(cancellationToken);

    if (user is null)
    {
      return RequestResponse<UserDto>.Fail("User not found.", 404);
    }

    var assigned = await roles.GetAsync(new[] { user.Id }, cancellationToken);
    return RequestResponse<UserDto>.Ok(user with { Role = assigned[user.Id] });
  }
}
