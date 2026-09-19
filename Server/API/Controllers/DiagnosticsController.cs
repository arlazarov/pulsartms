using Application.Features.Synchronization.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Admin")]
[Route("api/diagnostics/requests")]
public sealed class DiagnosticsController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetRequestDiagnosticsQuery(), cancellationToken);
}
