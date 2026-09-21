using Application.Models;

namespace Application.Features.Users.Commands;

public record RegisterUserCommand(
  string Name,
  string Email,
  string Password,
  string Role = "Dispatch"
) : IRequest<RequestResponse<Guid>>, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (string.IsNullOrWhiteSpace(Name) || Name.Length > 35)
      yield return "Enter a name of at most 35 characters.";
    if (
      string.IsNullOrWhiteSpace(Email)
      || !Text.LooksLikeEmail(Email)
      || Email.Length > 50
    )
      yield return "Enter a valid email address of at most 50 characters.";
    if (string.IsNullOrWhiteSpace(Password))
      yield return "Enter a password.";
    if (Role is not ("Admin" or "Dispatch"))
      yield return "Choose the Admin or Dispatch role.";
  }
}

public class RegisterUserHandler(
  IIdentityService identityService,
  IAppDbContext dbContext,
  IUserRoleService roles
) : IRequestHandler<RegisterUserCommand, RequestResponse<Guid>>
{
  public async Task<RequestResponse<Guid>> Handle(
    RegisterUserCommand request,
    CancellationToken cancellationToken
  )
  {
    await using var transaction =
      await dbContext.Database.BeginTransactionAsync(cancellationToken);
    var identityResult = await identityService.CreateUserAsync(
      request.Email,
      request.Password,
      cancellationToken
    );

    if (!identityResult.Success)
    {
      return RequestResponse<Guid>.Fail(
        identityResult.Errors ?? new ValidationErrors("Failed to create user.")
      );
    }

    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = identityResult.UserId!,
      Name = request.Name,
      Email = request.Email,
    };

    dbContext.Users.Add(user);
    await dbContext.SaveChangesAsync(cancellationToken);
    await roles.SetAsync(user.IdentityUserId, request.Role, cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return RequestResponse<Guid>.Ok(user.Id, 201);
  }
}
