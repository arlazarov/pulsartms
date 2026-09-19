using Application.Features.Costs.Commands;
using Application.Features.Costs.Models;
using Application.Features.Costs.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/costs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CostsController : BaseController
{
  [HttpGet("/api/dispatch/{id:guid}/costs")]
  public Task<IActionResult> ForLoad(Guid id, CancellationToken ct) =>
    HandleRequest(new GetLoadCostsQuery(id), ct);

  // The body states the complete set of shares, so this replaces the
  // attribution of the expense rather than adding to it.
  [HttpPut("expenses/{id:guid}/attribution")]
  public Task<IActionResult> SetAttribution(
    Guid id,
    [FromBody] ExpenseAttributionUpdate update,
    CancellationToken ct
  ) => HandleRequest(new SetExpenseAttributionCommand(id, update), ct);
}
