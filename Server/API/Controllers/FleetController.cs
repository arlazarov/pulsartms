using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Queries;
using Application.Features.Fleet.Commands.RequestTruckCamera;
using Application.Features.Fleet.Queries.GetTruckCamera;
using Application.Features.Fleet.Queries.GetLatestTruckCamera;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace API.Controllers;

[Route("api/[controller]")]
public class FleetController : BaseController
{
  [Authorize]
  [HttpGet("trucks/{truckId:guid}/camera")]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  public Task<IActionResult> LatestCamera(Guid truckId, CancellationToken ct) =>
    HandleRequest(new GetLatestTruckCameraQuery(truckId), ct);

  [Authorize]
  [HttpPost("trucks/{truckId:guid}/camera")]
  public Task<IActionResult> RequestCamera(Guid truckId, CancellationToken ct) =>
    HandleRequest(new RequestTruckCameraCommand(truckId), ct);

  [Authorize]
  [HttpGet("trucks/{truckId:guid}/camera/{requestId:guid}")]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  public Task<IActionResult> Camera(Guid truckId, Guid requestId, CancellationToken ct) =>
    HandleRequest(new GetTruckCameraQuery(truckId, requestId), ct);

  [Authorize]
  [HttpPost("sync")]
  public async Task<IActionResult> Sync(CancellationToken cancellationToken) =>
    await HandleRequest(new SyncFleetCommand(), cancellationToken);

  [HttpGet("drivers")]
  public async Task<IActionResult> GetDrivers(CancellationToken cancellationToken) =>
    await HandleRequest(new GetDriversQuery(), cancellationToken);

  [HttpGet("trailers")]
  public async Task<IActionResult> GetTrailers(CancellationToken cancellationToken) =>
    await HandleRequest(new GetTrailersQuery(), cancellationToken);

  [HttpGet("trucks")]
  public async Task<IActionResult> GetTrucks(CancellationToken cancellationToken) =>
    await HandleRequest(new GetTrucksQuery(), cancellationToken);

  [HttpGet("trucks/{truckId:guid}/history")]
  public async Task<IActionResult> History(Guid truckId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
    await HandleRequest(new GetTruckHistoryQuery(truckId, from, to), cancellationToken);

  [HttpGet("locations")]
  public async Task<IActionResult> GetLocations(CancellationToken cancellationToken, [FromQuery] bool points = true) =>
    await HandleRevisionedRequest(new GetFleetLocationsQuery(IncludePoints: points), x => x.Revision, cancellationToken);
}
