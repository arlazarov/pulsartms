using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/brokers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BrokersController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Search(
    [FromQuery] string search,
    CancellationToken ct
  ) => HandleRequest(new SearchBrokersQuery(search), ct);

  [HttpPut]
  public Task<IActionResult> Save(
    [FromBody] BrokerProfile profile,
    CancellationToken ct
  ) => HandleRequest(new SaveBrokerProfileCommand(profile), ct);
}
