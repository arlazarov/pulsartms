using Application.Features.Border;
using Application.Features.Border.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Admin")]
[Route("api/border")]
[RequestSizeLimit(12 * 1024 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BorderController : BaseController
{
  [HttpGet]
  public Task<IActionResult> List(int offset, CancellationToken ct) =>
    HandleRequest(new ListBorderCrossings(offset), ct);

  [HttpGet("{id:guid}")]
  public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
    HandleRequest(new GetBorderCrossing(id), ct);

  [HttpGet("ports")]
  public Task<IActionResult> Ports(CancellationToken ct) =>
    HandleRequest(new GetBorderPorts(), ct);

  [HttpGet("shipments")]
  public Task<IActionResult> Shipments(string search, CancellationToken ct) =>
    HandleRequest(new FindBorderShipments(search), ct);

  [HttpGet("assignments")]
  public Task<IActionResult> Assignments(Guid loadId, CancellationToken ct) =>
    HandleRequest(new GetBorderAssignments(loadId), ct);

  [HttpPut]
  public Task<IActionResult> Save(
    SaveBorderCrossing request,
    CancellationToken ct
  ) => HandleRequest(new SaveBorderCrossingCommand(request), ct);

  [HttpPost("check")]
  public Task<IActionResult> Check(
    BorderCrossing request,
    CancellationToken ct
  ) => HandleRequest(new CheckBorderCrossing(request), ct);
}
