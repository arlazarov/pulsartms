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
}
