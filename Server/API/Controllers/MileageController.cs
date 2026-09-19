using Application.Features.Mileage.Commands;
using Application.Features.Mileage.Models;
using Application.Features.Mileage.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/mileage")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MileageController : BaseController
{
  [HttpGet("/api/dispatch/{id:guid}/mileage-breakdown")]
  public Task<IActionResult> ForLoad(Guid id, CancellationToken ct) =>
    HandleRequest(new GetDispatchMileageQuery(id), ct);

  [HttpGet("/api/settings/mileage-policy")]
  public Task<IActionResult> Policy(CancellationToken ct) =>
    HandleRequest(new GetMileagePolicyQuery(), ct);

  [Authorize(Policy = "Admin")]
  [HttpPut("/api/settings/mileage-policy")]
  public Task<IActionResult> SavePolicy(
    [FromBody] MileagePolicyUpdate update,
    CancellationToken ct
  ) => HandleRequest(new UpdateMileagePolicyCommand(update), ct);

  [HttpGet("unallocated")]
  public Task<IActionResult> Unallocated(
    [FromQuery] int offset = 0,
    [FromQuery] int limit = 50,
    CancellationToken ct = default
  ) => HandleRequest(new GetUnallocatedMovementsQuery(offset, limit), ct);

  [HttpPost("movements")]
  public Task<IActionResult> Record(
    [FromBody] RecordMovementRequest request,
    CancellationToken ct
  ) => HandleRequest(new RecordMovementCommand(request), ct);

  [HttpPut("movements/{id:guid}/distance")]
  public Task<IActionResult> Distance(
    Guid id,
    [FromBody] MovementDistanceUpdate update,
    CancellationToken ct
  ) => HandleRequest(new UpdateMovementDistanceCommand(id, update), ct);

  [HttpPut("movements/{id:guid}/allocation")]
  public Task<IActionResult> Allocate(
    Guid id,
    [FromBody] MileageAllocationUpdate update,
    CancellationToken ct
  ) => HandleRequest(new UpdateMovementAllocationCommand(id, update), ct);
}
