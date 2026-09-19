using Application.Features.Fleet.Commands;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Admin")]
[Route("api/settings/fleet/{kind}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FleetConfigurationController : BaseController
{
  [HttpGet]
  public Task<IActionResult> List(
    string kind,
    [FromQuery] string? search,
    [FromQuery] int page = 1,
    CancellationToken ct = default
  ) => HandleRequest(new GetFleetConfigurationQuery(kind, search, page), ct);

  [HttpGet("{id:guid}")]
  public Task<IActionResult> Get(string kind, Guid id, CancellationToken ct) =>
    HandleRequest(new GetFleetResourceConfigurationQuery(kind, id), ct);

  [HttpPut("{id:guid}")]
  public Task<IActionResult> Save(
    string kind,
    Guid id,
    [FromBody] FleetConfigurationUpdate update,
    CancellationToken ct
  ) => HandleRequest(new UpdateFleetConfigurationCommand(kind, id, update), ct);
}
