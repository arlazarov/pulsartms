using Application.Features.Users.Commands;
using Application.Features.Users.Models;
using Application.Features.Users.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize]
[Route("api/settings/appearance")]
public sealed class AppearanceSettingsController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(CancellationToken cancellationToken) =>
    HandleRequest(new GetAppearanceSettingsQuery(), cancellationToken);

  [HttpPut]
  public Task<IActionResult> Save(
    AppearanceSettings request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      // A save names its theme; none is refused as a wrong one.
      new UpdateAppearanceSettingsCommand(
        request.Theme ?? "",
        request.TemperatureUnit,
        request.DistanceUnit
      ),
      cancellationToken
    );
}
