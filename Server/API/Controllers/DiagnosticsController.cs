using Application.Features.Synchronization.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Admin")]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : BaseController
{
  [HttpGet("memory/map")]
  public Task<IActionResult> MemoryMap(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetProcessMemoryMapQuery(), cancellationToken);

  [HttpGet("memory")]
  public Task<IActionResult> Memory(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetMemoryDiagnosticsQuery(), cancellationToken);

  [HttpGet("requests")]
  public Task<IActionResult> Get(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetRequestDiagnosticsQuery(), cancellationToken);

  // What a request spent its time on, rather than only how long it took.
  [HttpGet("stages")]
  public Task<IActionResult> Stages(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetStageDiagnosticsQuery(), cancellationToken);

  // Business-state audit: findings, coverage and the durable journal. It is
  // separate from liveness and readiness and never affects either.
  [HttpGet("consistency")]
  public Task<IActionResult> Consistency(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetConsistencyAuditQuery(), cancellationToken);

  [HttpGet("consistency/events")]
  public Task<IActionResult> ConsistencyEvents(
    [FromQuery] long after = 0,
    [FromQuery] int limit = 100,
    [FromQuery] string? kind = null,
    CancellationToken cancellationToken = default
  ) =>
    HandleUnwrappedRequest(
      new GetConsistencyEventsQuery(after, limit, kind),
      cancellationToken
    );

  [HttpGet("consistency/incidents")]
  public Task<IActionResult> ConsistencyIncidents(
    [FromQuery] Guid? after = null,
    [FromQuery] int limit = 100,
    CancellationToken cancellationToken = default
  ) =>
    HandleUnwrappedRequest(
      new GetConsistencyIncidentsQuery(after, limit),
      cancellationToken
    );

  [HttpPost("consistency/run")]
  public Task<IActionResult> RunConsistency(
    CancellationToken cancellationToken
  ) =>
    HandleUnwrappedRequest(new RunConsistencyAuditCommand(), cancellationToken);
}
