using Application.Features.Auth.Interfaces;
using Application.Models;

namespace Application.Features.Auth.Commands;

public record LoginCommand(string Email, string Password)
  : IRequest<RequestResponse<bool>>,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (string.IsNullOrWhiteSpace(Email) || !Text.LooksLikeEmail(Email))
      yield return "Enter the email address you signed up with.";
    if (Email?.Length > 50)
      yield return "That email address is too long.";
    if (string.IsNullOrWhiteSpace(Password))
      yield return "Enter your password.";
  }
}

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
