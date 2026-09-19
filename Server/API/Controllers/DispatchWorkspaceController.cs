using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/dispatch/{id:guid}/workspace")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DispatchWorkspaceController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
    HandleRequest(new GetDispatchWorkspaceQuery(id), ct);

  [HttpPut]
  public Task<IActionResult> Update(
    Guid id,
    [FromBody] UpdateDispatchWorkspaceRequest request,
    CancellationToken ct
  ) => HandleRequest(new UpdateDispatchWorkspaceCommand(id, request), ct);

  [HttpPost("verify-address")]
  public Task<IActionResult> VerifyAddress(
    Guid id,
    [FromBody] VerifyDispatchAddressRequest request,
    CancellationToken ct
  ) => HandleRequest(new VerifyDispatchAddressCommand(id, request), ct);
}
