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

  // Successful responses with a revision are revalidated by the browser; a matching
  // If-None-Match skips serializing an unchanged body. Private: bodies are per user.
  protected async Task<IActionResult> HandleRevisionedRequest<TData>(
    IRequest<RequestResponse<TData>> request,
    Func<TData, string?> revision,
    CancellationToken cancellationToken = default
  )
  {
    var result = await Mediator.Send(request, cancellationToken);
    if (!result.Success || result.Response is null || revision(result.Response) is not { Length: > 0 } current)
      return StatusCode(result.StatusCode, result);
    Response.Headers.CacheControl = "private, no-cache";
    Response.Headers.ETag = EntityTags.Weak(current);
    return EntityTags.Matches(Request.Headers.IfNoneMatch, current)
      ? StatusCode(StatusCodes.Status304NotModified)
      : StatusCode(result.StatusCode, result);
  }
}
