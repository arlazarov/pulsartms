using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class DispatchController : BaseController
{
  [Authorize(Policy = "Dispatch")]
  [HttpPost("verify-address")]
  public Task<IActionResult> VerifyNewLoadAddress(
    [FromBody] VerifyDispatchAddressRequest request,
    CancellationToken ct
  ) => HandleRequest(new VerifyDispatchAddressCommand(null, request), ct);

  [Authorize(Policy = "Dispatch")]
  [HttpPost]
  public Task<IActionResult> Create(
    [FromBody] CreateDispatchRequest request,
    CancellationToken cancellationToken
  ) => HandleRequest(new CreateDispatchCommand(request), cancellationToken);

  [Authorize(Policy = "Dispatch")]
  [HttpPut("{id:guid}/stops/{stopId:guid}/correction")]
  public Task<IActionResult> CorrectStop(
    Guid id,
    Guid stopId,
    [FromBody]
      Application.Features.Dispatch.Models.StopCorrectionRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new CorrectDispatchStopCommand(id, stopId, request),
      cancellationToken
    );

  [Authorize(Policy = "Dispatch")]
  [HttpPut("{id:guid}/stops/{stopId:guid}/operation")]
  public Task<IActionResult> SetStopOperation(
    Guid id,
    Guid stopId,
    [FromBody] StopOperationUpdate update,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SetStopOperationCommand(id, stopId, update),
      cancellationToken
    );

  [Authorize(Policy = "Dispatch")]
  [HttpPut("{id:guid}/truck-assignment")]
  public Task<IActionResult> SetTruckAssignment(
    Guid id,
    [FromBody] TruckAssignmentUpdate update,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(new SetTruckAssignmentCommand(id, update), cancellationToken);

  [Authorize(Policy = "Dispatch")]
  [HttpPut("{id:guid}/stops/{stopId:guid}/completion")]
  public Task<IActionResult> SetStopCompletion(
    Guid id,
    Guid stopId,
    [FromBody] StopCompletionUpdate update,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SetStopCompletionCommand(id, stopId, update),
      cancellationToken
    );

  [HttpGet("truck/{truckId:guid}/next-routes")]
  public Task<IActionResult> GetNextRoutes(
    Guid truckId,
    [FromQuery] Guid? currentDispatchId,
    [FromQuery] string? revision,
    [FromQuery] Guid? currentExecutionLegId,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new GetNextLoadRoutesQuery(
        truckId,
        currentDispatchId,
        revision,
        currentExecutionLegId
      ),
      cancellationToken
    );

  [Authorize]
  [HttpPost("sync")]
  public async Task<IActionResult> Sync(CancellationToken cancellationToken) =>
    await HandleRequest(new SyncDispatchesCommand(), cancellationToken);

  [HttpGet]
  public async Task<IActionResult> GetDispatch(
    [FromQuery] GetDispatchQuery query,
    CancellationToken cancellationToken
  ) => await HandleRequest(query, cancellationToken);

  [HttpGet("{id:guid}")]
  public Task<IActionResult> GetById(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetDispatchByIdQuery(id), cancellationToken);

  [HttpGet("board")]
  [CompressResponse]
  public Task<IActionResult> GetBoard(
    [FromQuery] GetDispatchBoardQuery query,
    CancellationToken cancellationToken
  ) => HandleRequest(query with { IdentitiesOnly = false }, cancellationToken);

  [Authorize]
  [HttpGet("board/enrichment")]
  [CompressResponse]
  public Task<IActionResult> Enrichment(
    [FromQuery] GetDispatchBoardQuery query,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new GetDispatchBoardEnrichmentQuery(query),
      cancellationToken
    );

  [Authorize]
  [HttpPost("board/planning")]
  [CompressResponse]
  public Task<IActionResult> PlanningSummaries(
    [FromBody] GetDispatchPlanningSummariesQuery query,
    CancellationToken cancellationToken
  ) => HandleRequest(query, cancellationToken);

  [Authorize]
  [HttpGet("board/telemetry")]
  public Task<IActionResult> Telemetry(
    [FromQuery] Guid[] truckIds,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(new GetDispatchTelemetryQuery(truckIds), cancellationToken);

  [HttpGet("truck/{truckId:guid}")]
  public async Task<IActionResult> GetTruckDispatch(
    Guid truckId,
    CancellationToken cancellationToken
  ) =>
    await HandleRequest(new GetTruckDispatchQuery(truckId), cancellationToken);
}
