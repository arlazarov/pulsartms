using Application.Features.Auth.Interfaces;
using Application.Models;

namespace Application.Features.Auth.Commands;

public class RefreshValidator : AbstractValidator<RefreshCommand>
{
  public RefreshValidator()
  {
    RuleFor(x => x.RefreshToken).NotEmpty();
  }
}

public record RefreshCommand(string RefreshToken) : IRequest<RequestResponse<bool>>;

public class RefreshHandler(IAuthService authService)
  : IRequestHandler<RefreshCommand, RequestResponse<bool>>
{
  public async Task<RequestResponse<bool>> Handle(
    RefreshCommand request,
    CancellationToken cancellationToken
  )
  {
    var success = await authService.RefreshAsync(request.RefreshToken, cancellationToken);

    return success
      ? RequestResponse<bool>.Ok(true)
      : RequestResponse<bool>.Fail("Invalid refresh token.", 401);
  }
}
