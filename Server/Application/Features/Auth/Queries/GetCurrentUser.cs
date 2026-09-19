using Application.Models;

namespace Application.Features.Auth.Queries;

public sealed record CurrentUserDto(
  Guid Id,
  string Name,
  string Email,
  bool IsAdmin
);

public sealed record GetCurrentUserQuery
  : IRequest<RequestResponse<CurrentUserDto>>;

public sealed class GetCurrentUserHandler(
  IAppDbContext db,
  ICurrentUser currentUser,
  IUserRoleService roles
) : IRequestHandler<GetCurrentUserQuery, RequestResponse<CurrentUserDto>>
{
  public async Task<RequestResponse<CurrentUserDto>> Handle(
    GetCurrentUserQuery request,
    CancellationToken cancellationToken
  )
  {
    var id = currentUser.IdentityUserId;
    if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(id))
      return Unauthorized();
    var user = await db
      .Users.AsNoTracking()
      .Where(x => x.IdentityUserId == id && x.IsActive)
      .Select(x => new
      {
        x.Id,
        x.Name,
        x.Email,
      })
      .SingleOrDefaultAsync(cancellationToken);
    if (user is null)
      return Unauthorized();
    var role = await roles.GetAsync(id, cancellationToken);
    return RequestResponse<CurrentUserDto>.Ok(
      new(user.Id, user.Name, user.Email, role == "Admin")
    );
  }

  private static RequestResponse<CurrentUserDto> Unauthorized() =>
    RequestResponse<CurrentUserDto>.Fail("Unauthorized.", 401);
}
