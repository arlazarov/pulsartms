using Application.Features.Execution.Commands;
using Application.Features.Execution.Queries;
using Domain.Models.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/execution/switches")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExecutionController : BaseController
{
  [HttpGet("/api/dispatch/{id:guid}/execution/switch-workspace")]
  public Task<IActionResult> Workspace(
    Guid id,
    [FromQuery] Guid? secondDispatchId,
    CancellationToken ct
  ) => HandleRequest(new GetSwitchWorkspaceQuery(id, secondDispatchId), ct);

  [HttpGet("/api/dispatch/{id:guid}/execution/source-review")]
  public Task<IActionResult> ReviewSource(
    Guid id,
    [FromQuery] Guid executionLegId,
    CancellationToken ct
  ) => HandleRequest(new GetExecutionSourceReviewQuery(id, executionLegId), ct);

  [HttpPost("/api/dispatch/{id:guid}/execution/source-review")]
  public Task<IActionResult> AcceptSource(
    Guid id,
    [FromBody] AcceptExecutionSourceChangesRequest request,
    CancellationToken ct
  ) => HandleRequest(new AcceptExecutionSourceChangesCommand(id, request), ct);

  [HttpGet("{id:guid}")]
  public Task<IActionResult> Details(Guid id, CancellationToken ct) =>
    HandleRequest(new GetSwitchDetailsQuery(id), ct);

  [HttpPost("preview")]
  public Task<IActionResult> Preview(
    [FromBody] PlanSwitchRequest request,
    CancellationToken ct
  ) => HandleRequest(new PreviewSwitchQuery(request), ct);

  [HttpPost]
  public Task<IActionResult> Plan(
    [FromBody] PlanSwitchRequest request,
    CancellationToken ct
  ) => HandleRequest(new PlanSwitchCommand(request), ct);

  [HttpPost("{id:guid}/participants/{participantId:guid}/release")]
  public Task<IActionResult> Release(
    Guid id,
    Guid participantId,
    [FromBody] SwitchParticipantAction action,
    CancellationToken ct
  ) =>
    HandleRequest(
      new ReleaseSwitchParticipantCommand(
        action with
        {
          SwitchId = id,
          ParticipantId = participantId,
        }
      ),
      ct
    );

  [HttpPost("{id:guid}/participants/{participantId:guid}/receive")]
  public Task<IActionResult> Receive(
    Guid id,
    Guid participantId,
    [FromBody] SwitchParticipantAction action,
    CancellationToken ct
  ) =>
    HandleRequest(
      new ReceiveSwitchParticipantCommand(
        action with
        {
          SwitchId = id,
          ParticipantId = participantId,
        }
      ),
      ct
    );

  [HttpPost("{id:guid}/cancel")]
  public Task<IActionResult> Cancel(
    Guid id,
    [FromBody] CancelSwitchRequest request,
    CancellationToken ct
  ) => HandleRequest(new CancelSwitchCommand(id, request), ct);
}
