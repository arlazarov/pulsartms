using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Queries;
using Application.Features.Fleet.Commands.RequestTruckCamera;
using Application.Features.Fleet.Queries.GetTruckCamera;
using Application.Features.Fleet.Queries.GetLatestTruckCamera;
using Application.Features.Fleet.Queries.GetFleetLocations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

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

  // Server-sent events for hosts that pass streamed responses through (a direct Cloud Run
  // domain). The Client uses held polls because Firebase Hosting rewrites buffer streams.
  // Each event carries the same wrapped body as GET locations; unchanged waits send a comment.
  [HttpGet("locations/stream")]
  public async Task LocationsStream(CancellationToken cancellationToken, [FromQuery] bool points = true)
  {
    Response.StatusCode = StatusCodes.Status200OK;
    Response.ContentType = "text/event-stream";
    Response.Headers.CacheControl = "no-cache";
    var json = HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    await Response.StartAsync(cancellationToken);
    string? revision = null;
    while (!cancellationToken.IsCancellationRequested)
    {
      var result = await Mediator.Send(new GetFleetLocationsQuery(IncludePoints: points, KnownRevision: revision, WaitSeconds: 25), cancellationToken);
      if (!result.Success || result.Response is null) break;
      if (result.Response.Revision is null || result.Response.Revision == revision)
        await Response.WriteAsync(": waiting\n\n", cancellationToken);
      else
      {
        revision = result.Response.Revision;
        await Response.WriteAsync($"id: {revision}\nevent: locations\ndata: ", cancellationToken);
        await Response.WriteAsync(JsonSerializer.Serialize(result, json), cancellationToken);
        await Response.WriteAsync("\n\n", cancellationToken);
      }
      await Response.Body.FlushAsync(cancellationToken);
    }
  }

  [HttpGet("locations")]
  public async Task<IActionResult> GetLocations(CancellationToken cancellationToken, [FromQuery] bool points = true, [FromQuery] int wait = 0) =>
    await HandleRevisionedRequest(new GetFleetLocationsQuery(IncludePoints: points,
      KnownRevision: EntityTags.Revision(Request.Headers.IfNoneMatch), WaitSeconds: Math.Clamp(wait, 0, 60)), x => x.Revision, cancellationToken);
}
