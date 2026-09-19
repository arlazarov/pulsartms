using Application.Features.Auth.Interfaces;
using Application.Models;

namespace Application.Features.Auth.Commands;

public class LoginValidator : AbstractValidator<LoginCommand>
{
  public LoginValidator()
  {
    RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(50);
    RuleFor(x => x.Password).NotEmpty();
  }
}

public record LoginCommand(string Email, string Password)
  : IRequest<RequestResponse<bool>>;

public class LoginHandler(IAuthService authService)
  : IRequestHandler<LoginCommand, RequestResponse<bool>>
{
  public async Task<RequestResponse<bool>> Handle(
    LoginCommand request,
    CancellationToken cancellationToken
  )
  {
    var success = await authService.LoginAsync(
      request.Email,
      request.Password,
      cancellationToken
    );

    return success
      ? RequestResponse<bool>.Ok(true)
      : RequestResponse<bool>.Fail("Invalid email or password.", 401);
  }
}
