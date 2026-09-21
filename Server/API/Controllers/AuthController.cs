using Application.Features.Auth.Commands;
using Application.Features.Auth.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[Route("api/[controller]")]
public class AuthController : BaseController
{
  [HttpGet("me")]
  public Task<IActionResult> Me(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetCurrentUserQuery(), cancellationToken);

  [AllowAnonymous]
  [EnableRateLimiting(RequestLimits.SignIn)]
  [HttpPost("login")]
  public async Task<IActionResult> Login(
    LoginCommand command,
    CancellationToken cancellationToken
  )
  {
    var result = await Mediator.Send(command, cancellationToken);
    return result.Success
      ? new EmptyResult()
      : StatusCode(result.StatusCode, result);
  }

  [AllowAnonymous]
  [EnableRateLimiting(RequestLimits.SignIn)]
  [HttpPost("refresh")]
  public async Task<IActionResult> Refresh(
    RefreshCommand command,
    CancellationToken cancellationToken
  )
  {
    var result = await Mediator.Send(command, cancellationToken);
    return result.Success
      ? new EmptyResult()
      : StatusCode(result.StatusCode, result);
  }

  [Authorize]
  [HttpPost("logout")]
  public async Task<IActionResult> Logout(
    CancellationToken cancellationToken
  ) => await HandleRequest(new LogoutCommand(), cancellationToken);
}
