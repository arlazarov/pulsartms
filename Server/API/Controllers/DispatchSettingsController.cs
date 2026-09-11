using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize]
[Route("api/settings/dispatch")]
public sealed class DispatchSettingsController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(CancellationToken cancellationToken) =>
    HandleRequest(new GetDispatchSettingsQuery(), cancellationToken);

  [HttpPut]
  [Authorize(Policy = "Admin")]
  public Task<IActionResult> Save(UpdateDispatchSettingsCommand request, CancellationToken cancellationToken) =>
    HandleRequest(request, cancellationToken);
}
