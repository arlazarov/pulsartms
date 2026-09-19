using System.Security.Cryptography;
using System.Text.Json;
using Application.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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

  // Results without a cheap revision are serialized once and tagged with a digest of the body, so a
  // poll whose If-None-Match still matches transfers no body. The handler still runs in full.
  protected async Task<IActionResult> HandleDigestedRequest<TData>(
    IRequest<RequestResponse<TData>> request,
    CancellationToken cancellationToken = default
  )
  {
    var result = await Mediator.Send(request, cancellationToken);
    if (!result.Success) return StatusCode(result.StatusCode, result);
    var options = HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    var body = JsonSerializer.SerializeToUtf8Bytes(result, options);
    var digest = Convert.ToHexStringLower(SHA256.HashData(body))[..32];
    Response.Headers.CacheControl = "private, no-cache";
    Response.Headers.ETag = EntityTags.Weak(digest);
    return EntityTags.Matches(Request.Headers.IfNoneMatch, digest)
      ? StatusCode(StatusCodes.Status304NotModified)
      : new FileContentResult(body, "application/json; charset=utf-8");
  }
}
