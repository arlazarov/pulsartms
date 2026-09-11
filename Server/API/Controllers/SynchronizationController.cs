using Application.Features.Synchronization.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize]
[Route("api/synchronization/status")]
public sealed class SynchronizationController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(CancellationToken cancellationToken) =>
    HandleUnwrappedRequest(new GetSynchronizationStatusQuery(), cancellationToken);
}
