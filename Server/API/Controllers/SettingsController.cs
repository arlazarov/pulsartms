using Application.Features.Routing.Commands;
using Application.Features.Routing.Models;
using Application.Features.Routing.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Admin")]
[Route("api/settings/planning")]
public class SettingsController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(CancellationToken cancellationToken) =>
    HandleRequest(new GetPlanningSettingsQuery(), cancellationToken);

  [HttpPut]
  public Task<IActionResult> Save(
    PlanningSettingsUpdate request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new UpdatePlanningSettingsCommand(request),
      cancellationToken
    );
}
