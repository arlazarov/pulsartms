using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace API.Controllers;

[Route("api/[controller]")]
public class DispatchController : BaseController
{
  [HttpGet("truck/{truckId:guid}/next-routes")]
  public Task<IActionResult> GetNextRoutes(Guid truckId, [FromQuery] Guid? currentDispatchId, [FromQuery] string? revision, CancellationToken cancellationToken) =>
    HandleRequest(new Application.Features.Routing.Queries.GetNextLoadRoutesQuery(truckId, currentDispatchId, revision), cancellationToken);

  [Authorize]
  [HttpPost("sync")]
  public async Task<IActionResult> Sync(CancellationToken cancellationToken) =>
    await HandleRequest(new SyncDispatchesCommand(), cancellationToken);

  [HttpGet]
  public async Task<IActionResult> GetDispatch(
    [FromQuery] GetDispatchQuery query, CancellationToken cancellationToken) =>
    await HandleRequest(query, cancellationToken);

  [HttpGet("{id:guid}")]
  public Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
    HandleRequest(new GetDispatchByIdQuery(id), cancellationToken);

  [HttpGet("board")]
  public Task<IActionResult> GetBoard([FromQuery] GetDispatchBoardQuery query, CancellationToken cancellationToken) =>
    HandleRequest(query, cancellationToken);

  [HttpGet("truck/{truckId:guid}")]
  public async Task<IActionResult> GetTruckDispatch(
    Guid truckId,
    CancellationToken cancellationToken
  ) => await HandleRequest(new GetTruckDispatchQuery(truckId), cancellationToken);
}
