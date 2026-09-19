using Application.Features.Routing.Commands;
using Application.Features.Routing.Models;
using Application.Features.Routing.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize]
[Route("api/dispatch/{dispatchId:guid}/planning")]
public class RoutePlanningController : BaseController
{
  [HttpGet("base")]
  public Task<IActionResult> GetBase(Guid dispatchId, CancellationToken cancellationToken) =>
    HandleRequest(new GetBaseRouteQuery(dispatchId), cancellationToken);
  [HttpGet("/api/fleet/planning/previews")]
  public Task<IActionResult> Previews(CancellationToken cancellationToken) =>
    HandleRequest(new GetRoutePreviewsQuery(), cancellationToken);

  [HttpGet("/api/fleet/trucks/{truckId:guid}/planning/preview")]
  public Task<IActionResult> TruckPreview(Guid truckId, CancellationToken cancellationToken) =>
    HandleRequest(new GetTruckRoutePreviewQuery(truckId), cancellationToken);

  // POST remains for browsers still running a Client that polled planning with it.
  [HttpGet("automatic")]
  [HttpPost("automatic")]
  public Task<IActionResult> Automatic(Guid dispatchId, CancellationToken cancellationToken,
    [FromQuery] Guid? knownPlanId = null, [FromQuery] int? knownVersion = null) =>
    HandleDigestedRequest(new GetDispatchPlanningQuery(dispatchId, knownPlanId, knownVersion), cancellationToken);

  [HttpGet("/api/fleet/trucks/{truckId:guid}/planning")]
  [HttpPost("/api/fleet/trucks/{truckId:guid}/planning")]
  public Task<IActionResult> Truck(Guid truckId, CancellationToken cancellationToken,
    [FromQuery] Guid? knownPlanId = null, [FromQuery] int? knownVersion = null) =>
    HandleDigestedRequest(new GetTruckPlanningQuery(truckId, knownPlanId, knownVersion), cancellationToken);

  [HttpGet]
  public Task<IActionResult> Get(Guid dispatchId, CancellationToken cancellationToken) =>
    HandleRequest(new GetRoutePlanningQuery(dispatchId), cancellationToken);

  [HttpPut("profile")]
  public Task<IActionResult> Profile(Guid dispatchId, TruckRouteProfile request, CancellationToken cancellationToken) =>
    HandleRequest(new SaveTruckRouteProfileCommand(dispatchId, request), cancellationToken);

  [HttpPost("route")]
  public Task<IActionResult> Route(Guid dispatchId, RouteBuildRequest request, CancellationToken cancellationToken) =>
    HandleRequest(new BuildRouteCommand(dispatchId, request), cancellationToken);

  [HttpPost("fuel")]
  public Task<IActionResult> Fuel(Guid dispatchId, FuelBuildRequest request, CancellationToken cancellationToken) =>
    HandleRequest(new BuildFuelPlanCommand(dispatchId, request), cancellationToken);

  [HttpPost("fuel/recalculate")]
  public Task<IActionResult> RecalculateFuel(Guid dispatchId, CancellationToken cancellationToken) =>
    HandleRequest(new RecalculateFuelPlanCommand(dispatchId), cancellationToken);

  [HttpPost("fuel/edit/preview")]
  public Task<IActionResult> PreviewFuelEdit(Guid dispatchId, FuelPlanEditRequest request, CancellationToken cancellationToken) =>
    HandleRequest(new EditFuelPlanCommand(dispatchId, request, false), cancellationToken);

  [HttpPut("fuel/edit")]
  public Task<IActionResult> SaveFuelEdit(Guid dispatchId, FuelPlanEditRequest request, CancellationToken cancellationToken) =>
    HandleRequest(new EditFuelPlanCommand(dispatchId, request, true), cancellationToken);

  [HttpPost("fuel/reset")]
  public Task<IActionResult> ResetFuel(Guid dispatchId, ResetFuelPlanRequest request, CancellationToken cancellationToken) =>
    HandleRequest(new ResetFuelPlanCommand(dispatchId, request), cancellationToken);
}
