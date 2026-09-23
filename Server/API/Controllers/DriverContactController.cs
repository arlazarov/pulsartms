using Application.Features.Fleet.Commands;
using Application.Features.Fleet.Queries;
using Domain.Models.Fleet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/drivers/{driverId:guid}/contact")]
public class DriverContactController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(Guid driverId, CancellationToken ct) =>
    HandleRequest(new GetDriverContactQuery(driverId), ct);

  [HttpPut]
  public Task<IActionResult> Update(
    Guid driverId,
    DriverContactUpdate update,
    CancellationToken ct
  ) => HandleRequest(new UpdateDriverContactCommand(driverId, update), ct);
}
