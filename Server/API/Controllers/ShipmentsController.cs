using Application.Features.Shipments;
using Application.Features.Shipments.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/shipments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShipmentsController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(Guid loadId, CancellationToken ct) =>
    HandleRequest(new GetShipments(loadId), ct);

  [HttpGet("stops")]
  public Task<IActionResult> Stops(Guid loadId, CancellationToken ct) =>
    HandleRequest(new GetShipmentStops(loadId), ct);

  [HttpPut]
  public Task<IActionResult> Save(SaveShipment request, CancellationToken ct) =>
    HandleRequest(new SaveShipmentCommand(request), ct);

  [HttpPost("check")]
  public Task<IActionResult> Check(Shipment request, CancellationToken ct) =>
    HandleRequest(new CheckShipment(request), ct);
}
