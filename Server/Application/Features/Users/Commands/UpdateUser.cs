using Application.Caching;
using Application.Models;

namespace Application.Features.Users.Commands;

public record UpdateUserCommand(
  Guid Id,
  string? Name,
  string? Email,
  string? Password,
  bool? IsActive,
  string? Role = null
) : IRequest<RequestResponse<Guid>>, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (Id == Guid.Empty)
      yield return "Choose a user.";
    if (Role is not (null or "Admin" or "Dispatch"))
      yield return "Choose the Admin or Dispatch role.";
    if (
      Name is not null
      && (string.IsNullOrWhiteSpace(Name) || Name.Length > 35)
    )
      yield return "Enter a name of at most 35 characters.";
    if (Email is not null && (!Text.LooksLikeEmail(Email) || Email.Length > 50))
      yield return "Enter a valid email address of at most 50 characters.";
  }
}

public class UpdateUserHandler(
  IAppDbContext dbContext,
  IIdentityService identityService,
  IUserRoleService roles,
  ICurrentUser currentUser,
  ReadCache reads
) : IRequestHandler<UpdateUserCommand, RequestResponse<Guid>>
{
  public async Task<RequestResponse<Guid>> Handle(
    UpdateUserCommand request,
    CancellationToken cancellationToken
  )
  {
    if (
      !currentUser.IsAuthenticated
      || string.IsNullOrWhiteSpace(currentUser.IdentityUserId)
    )
      return RequestResponse<Guid>.Fail("Unauthorized.", 401);
    await using var transaction =
      await dbContext.Database.BeginTransactionAsync(cancellationToken);
    var user = await dbContext.Users.FirstOrDefaultAsync(
      x => x.Id == request.Id,
      cancellationToken
    );

    if (user is null)
    {
      return RequestResponse<Guid>.Fail("User not found.", 404);
    }

    if (user.IdentityUserId == currentUser.IdentityUserId)
    {
      if (request.Role == "Dispatch")
        return RequestResponse<Guid>.Fail(
          "You cannot remove your own Admin role."
        );
      if (request.IsActive == false)
        return RequestResponse<Guid>.Fail(
          "You cannot deactivate your own account."
        );
    }

    if (request.Email is not null && request.Email != user.Email)
    {
      var identityResult = await identityService.UpdateEmailAsync(
        user.IdentityUserId,
        request.Email,
        cancellationToken
      );

      if (!identityResult.Success)
      {
        return RequestResponse<Guid>.Fail(
          identityResult.Errors
            ?? new ValidationErrors("Failed to update email.")
        );
      }

      user.Email = request.Email;
    }

    if (request.Password is not null)
    {
      var identityResult = await identityService.UpdatePasswordAsync(
        user.IdentityUserId,
        request.Password,
        cancellationToken
      );

      if (!identityResult.Success)
      {
        return RequestResponse<Guid>.Fail(
          identityResult.Errors
            ?? new ValidationErrors("Failed to update password.")
        );
      }
    }

    if (request.IsActive.HasValue && request.IsActive.Value != user.IsActive)
    {
      var identityResult = await identityService.SetActiveAsync(
        user.IdentityUserId,
        request.IsActive.Value,
        cancellationToken
      );

      if (!identityResult.Success)
      {
        return RequestResponse<Guid>.Fail(
          identityResult.Errors
            ?? new ValidationErrors("Failed to update user status.")
        );
      }

      user.IsActive = request.IsActive.Value;
    }

    if (request.Name is not null)
    {
      user.Name = request.Name;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    if (request.Role is not null)
      await roles.SetAsync(
        user.IdentityUserId,
        request.Role,
        cancellationToken
      );
    await transaction.CommitAsync(cancellationToken);
    reads.Invalidate($"session:{user.IdentityUserId}");

    return RequestResponse<Guid>.Ok(user.Id);
  }
}
