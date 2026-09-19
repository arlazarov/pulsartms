using Application.Caching;
using Application.Models;

namespace Application.Features.Users.Commands;

public record DeleteUserCommand(Guid Id) : IRequest<RequestResponse<Guid>>;

public class DeleteUserHandler(
  IAppDbContext dbContext,
  IIdentityService identityService,
  ICurrentUser currentUser,
  ReadCache reads
) : IRequestHandler<DeleteUserCommand, RequestResponse<Guid>>
{
  public async Task<RequestResponse<Guid>> Handle(
    DeleteUserCommand request,
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
      return RequestResponse<Guid>.Fail("You cannot delete your own account.");

    var identityResult = await identityService.DeleteUserAsync(
      user.IdentityUserId,
      cancellationToken
    );

    if (!identityResult.Success)
    {
      return RequestResponse<Guid>.Fail(
        identityResult.Errors
          ?? new ValidationErrors("Failed to delete identity user.")
      );
    }

    dbContext.Users.Remove(user);
    await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    reads.Invalidate($"session:{user.IdentityUserId}");

    return RequestResponse<Guid>.Ok(user.Id);
  }
}
