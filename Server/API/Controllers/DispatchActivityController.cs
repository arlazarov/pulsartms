using Application.Features.Dispatch.Activity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/dispatch/{dispatchId:guid}/activity")]
public sealed class DispatchActivityController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(
    Guid dispatchId,
    [FromQuery] long? beforeRevision,
    [FromQuery] bool openOnly,
    CancellationToken ct
  ) =>
    HandleRequest(
      new GetDispatchActivityQuery(dispatchId, beforeRevision, openOnly),
      ct
    );

  [HttpPost]
  public Task<IActionResult> Add(
    Guid dispatchId,
    [FromBody] AddDispatchActivityUpdate update,
    CancellationToken ct
  ) => HandleRequest(new AddDispatchActivityCommand(dispatchId, update), ct);

  [HttpPost("{entryId:guid}/resolve")]
  public Task<IActionResult> Resolve(
    Guid dispatchId,
    Guid entryId,
    [FromBody] ResolveDispatchActivityUpdate update,
    CancellationToken ct
  ) =>
    HandleRequest(
      new ResolveDispatchActivityCommand(dispatchId, entryId, update),
      ct
    );
}
