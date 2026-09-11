using Application.Features.Auth.Interfaces;
using Application.Models;

namespace Application.Features.Auth.Commands;

public record LogoutCommand : IRequest<RequestResponse<bool>>;

public class LogoutHandler(IAuthService authService)
  : IRequestHandler<LogoutCommand, RequestResponse<bool>>
{
  public async Task<RequestResponse<bool>> Handle(
    LogoutCommand request,
    CancellationToken cancellationToken
  )
  {
    await authService.LogoutAsync();

    return RequestResponse<bool>.Ok(true);
  }
}
