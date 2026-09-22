using Application.Features.Fleet.Commands.RequestTruckCamera;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Queries;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Queries.GetLatestTruckCamera;
using Application.Features.Fleet.Queries.GetTruckCamera;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
[CompressResponse]
public class FleetController : BaseController
{
  [Authorize]
  [HttpGet("trucks/{truckId:guid}/weather")]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  public Task<IActionResult> Weather(Guid truckId, CancellationToken ct) =>
    HandleRequest(new GetTruckWeatherQuery(truckId), ct);

  [Authorize]
  [HttpGet("hos")]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  public Task<IActionResult> Hos(
    [FromQuery] Guid[]? truckIds,
    CancellationToken ct
  ) =>
    HandleRequest(
      new GetFleetHosQuery(truckIds is { Length: > 0 } ? truckIds : null),
      ct
    );

  [Authorize]
  [HttpGet("trucks/{truckId:guid}/camera")]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  public Task<IActionResult> LatestCamera(Guid truckId, CancellationToken ct) =>
    HandleRequest(new GetLatestTruckCameraQuery(truckId), ct);

  [Authorize]
  [HttpPost("trucks/{truckId:guid}/camera")]
  public Task<IActionResult> RequestCamera(
    Guid truckId,
    CancellationToken ct
  ) => HandleRequest(new RequestTruckCameraCommand(truckId), ct);

  [Authorize]
  [HttpGet("trucks/{truckId:guid}/camera/{requestId:guid}")]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  public Task<IActionResult> Camera(
    Guid truckId,
    Guid requestId,
    CancellationToken ct
  ) => HandleRequest(new GetTruckCameraQuery(truckId, requestId), ct);

  [Authorize]
  [HttpPost("sync")]
  public async Task<IActionResult> Sync(CancellationToken cancellationToken) =>
    await HandleRequest(new SyncFleetCommand(), cancellationToken);

  [HttpGet("drivers")]
  public async Task<IActionResult> GetDrivers(
    CancellationToken cancellationToken
  ) => await HandleRequest(new GetDriversQuery(), cancellationToken);

  [HttpGet("trailers")]
  public async Task<IActionResult> GetTrailers(
    CancellationToken cancellationToken
  ) => await HandleRequest(new GetTrailersQuery(), cancellationToken);

  [HttpGet("trucks")]
  public async Task<IActionResult> GetTrucks(
    CancellationToken cancellationToken
  ) => await HandleRequest(new GetTrucksQuery(), cancellationToken);

  [HttpGet("trucks/{truckId:guid}/history")]
  public async Task<IActionResult> History(
    Guid truckId,
    DateTimeOffset from,
    DateTimeOffset to,
    CancellationToken cancellationToken
  ) =>
    await HandleRequest(
      new GetTruckHistoryQuery(truckId, from, to),
      cancellationToken
    );

  [Authorize]
  [HttpGet("trucks/{truckId:guid}/movement")]
  public Task<IActionResult> Movement(
    Guid truckId,
    DateTimeOffset from,
    DateTimeOffset to,
    CancellationToken ct
  ) => HandleRequest(new GetTruckMovementQuery(truckId, from, to), ct);

  [HttpGet("locations")]
  public async Task<IActionResult> GetLocations(
    CancellationToken cancellationToken
  ) => await HandleRequest(new GetFleetLocationsQuery(), cancellationToken);
}
