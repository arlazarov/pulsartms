using Application.Features.Routing.Commands;
using Application.Features.Routing.Queries;
using Domain.Models.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[Authorize]
[CompressResponse]
[EnableRateLimiting(RequestLimits.Planning)]
[Route("api/dispatch/{dispatchId:guid}/planning")]
public class RoutePlanningController : BaseController
{
  [Authorize(Policy = "Dispatch")]
  [HttpPost("route/options")]
  public Task<IActionResult> RouteOptions(
    Guid dispatchId,
    RouteChoiceRequest request,
    CancellationToken ct
  ) => HandleRequest(new PreviewRouteChoiceCommand(dispatchId, request), ct);

  [Authorize(Policy = "Dispatch")]
  [HttpPut("route/choice")]
  public Task<IActionResult> ChooseRoute(
    Guid dispatchId,
    RouteChoiceSave request,
    CancellationToken ct
  ) => HandleRequest(new SaveRouteChoiceCommand(dispatchId, request), ct);

  [Authorize(Policy = "Dispatch")]
  [HttpPost("route/via-location")]
  public Task<IActionResult> LocateVia(
    Guid dispatchId,
    LocateRouteViaCommand request,
    CancellationToken ct
  ) => HandleRequest(request, ct);

  [HttpGet("base")]
  public Task<IActionResult> GetBase(
    Guid dispatchId,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetBaseRouteQuery(dispatchId), cancellationToken);

  [Authorize(Policy = "Dispatch")]
  [HttpGet("map")]
  public Task<IActionResult> Map(Guid dispatchId, CancellationToken ct) =>
    HandleRequest(new GetDispatchMapRouteQuery(dispatchId), ct);

  [HttpGet("/api/fleet/planning/previews")]
  public Task<IActionResult> Previews(CancellationToken cancellationToken) =>
    HandleRequest(new GetRoutePreviewsQuery(), cancellationToken);

  [HttpGet("/api/fleet/trucks/{truckId:guid}/planning/preview")]
  public Task<IActionResult> TruckPreview(
    Guid truckId,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetTruckRoutePreviewQuery(truckId), cancellationToken);

  [HttpPost("automatic")]
  public Task<IActionResult> Automatic(
    Guid dispatchId,
    CancellationToken cancellationToken,
    [FromQuery] Guid? knownPlanId = null,
    [FromQuery] int? knownVersion = null
  ) =>
    HandleRequest(
      new GetDispatchPlanningQuery(dispatchId, knownPlanId, knownVersion),
      cancellationToken
    );

  [HttpPost("/api/fleet/trucks/{truckId:guid}/planning")]
  public Task<IActionResult> Truck(
    Guid truckId,
    CancellationToken cancellationToken,
    [FromQuery] Guid? knownPlanId = null,
    [FromQuery] int? knownVersion = null
  ) =>
    HandleRequest(
      new GetTruckPlanningQuery(truckId, knownPlanId, knownVersion),
      cancellationToken
    );

  [HttpGet]
  public Task<IActionResult> Get(
    Guid dispatchId,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetRoutePlanningQuery(dispatchId), cancellationToken);

  [HttpPut("profile")]
  public Task<IActionResult> Profile(
    Guid dispatchId,
    TruckRouteProfile request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SaveTruckRouteProfileCommand(dispatchId, request),
      cancellationToken
    );

  [HttpPost("route")]
  public Task<IActionResult> Route(
    Guid dispatchId,
    RouteBuildRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new BuildRouteCommand(dispatchId, request),
      cancellationToken
    );

  [HttpPost("fuel")]
  public Task<IActionResult> Fuel(
    Guid dispatchId,
    FuelBuildRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new BuildFuelPlanCommand(dispatchId, request),
      cancellationToken
    );

  [Authorize(Policy = "Dispatch")]
  [HttpPost("fuel/recalculate")]
  public Task<IActionResult> RecalculateFuel(
    Guid dispatchId,
    CancellationToken cancellationToken,
    [FromQuery] Guid? executionLegId = null,
    [FromQuery] long? assignmentRevision = null
  ) =>
    HandleRequest(
      new RecalculateFuelPlanCommand(
        dispatchId,
        executionLegId,
        assignmentRevision
      ),
      cancellationToken
    );

  [HttpPost("fuel/edit/preview")]
  public Task<IActionResult> PreviewFuelEdit(
    Guid dispatchId,
    FuelPlanEditRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new EditFuelPlanCommand(dispatchId, request, false),
      cancellationToken
    );

  [HttpPut("fuel/edit")]
  public Task<IActionResult> SaveFuelEdit(
    Guid dispatchId,
    FuelPlanEditRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new EditFuelPlanCommand(dispatchId, request, true),
      cancellationToken
    );

  [HttpPost("fuel/reset")]
  public Task<IActionResult> ResetFuel(
    Guid dispatchId,
    ResetFuelPlanRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new ResetFuelPlanCommand(dispatchId, request),
      cancellationToken
    );
}
