using Application.Models;

namespace Application.Features.Users.Commands;

public class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
  public RegisterUserValidator()
  {
    RuleFor(x => x.Name).NotEmpty().MaximumLength(35);
    RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(50);
    RuleFor(x => x.Password).NotEmpty();
    RuleFor(x => x.Role).Must(x => x is "Admin" or "Dispatch");
  }
}

public record RegisterUserCommand(string Name, string Email, string Password, string Role = "Dispatch")
  : IRequest<RequestResponse<Guid>>;

public class RegisterUserHandler(IIdentityService identityService, IAppDbContext dbContext, IUserRoleService roles)
  : IRequestHandler<RegisterUserCommand, RequestResponse<Guid>>
{
  public async Task<RequestResponse<Guid>> Handle(
    RegisterUserCommand request,
    CancellationToken cancellationToken
  )
  {
    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
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
