using Application.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
  private IMediator? _mediator;

  // Existing endpoints expose an unwrapped success body; keep their wire contract.
  protected async Task<IActionResult> HandleUnwrappedRequest<TData>(
    IRequest<RequestResponse<TData>> request, CancellationToken cancellationToken = default)
  {
    var result = await Mediator.Send(request, cancellationToken);
    if (result.StatusCode == 401) return Unauthorized();
    return StatusCode(result.StatusCode, result.Success ? (object?)result.Response : result);
  }

  protected IMediator Mediator =>
    _mediator ??= HttpContext.RequestServices.GetRequiredService<IMediator>();

  protected async Task<IActionResult> HandleRequest<TData>(
    IRequest<RequestResponse<TData>> request,
    CancellationToken cancellationToken = default
  )
  {
    var result = await Mediator.Send(request, cancellationToken);

    return StatusCode(result.StatusCode, result);
  }
}
