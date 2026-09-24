using Application.Features.DriverGroups.Commands;
using Application.Features.DriverGroups.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// A dispatcher's own driver groups and the one chosen for every page. Only
// a filter: it grants nothing and narrows only what the dispatcher sees.
[Authorize(Policy = "Dispatch")]
[Route("api/driver-groups")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverGroupsController : BaseController
{
  public sealed record GroupRequest(
    string Name,
    IReadOnlyList<Guid> Drivers,
    long Revision
  );

  public sealed record SelectionRequest(Guid? GroupId);

  [HttpGet]
  public Task<IActionResult> Get(CancellationToken ct) =>
    HandleRequest(new GetDriverGroupsQuery(), ct);

  [HttpPost]
  [RequestSizeLimit(65_536)]
  public Task<IActionResult> Create(
    GroupRequest request,
    CancellationToken ct
  ) =>
    HandleRequest(
      new SaveDriverGroupCommand(null, request.Name, request.Drivers, 0),
      ct
    );

  [HttpPut("{id:guid}")]
  [RequestSizeLimit(65_536)]
  public Task<IActionResult> Update(
    Guid id,
    GroupRequest request,
    CancellationToken ct
  ) =>
    HandleRequest(
      new SaveDriverGroupCommand(
        id,
        request.Name,
        request.Drivers,
        request.Revision
      ),
      ct
    );

  [HttpDelete("{id:guid}")]
  public Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
    HandleRequest(new DeleteDriverGroupCommand(id), ct);

  [HttpPut("selection")]
  public Task<IActionResult> Select(
    SelectionRequest request,
    CancellationToken ct
  ) => HandleRequest(new SelectDriverGroupCommand(request.GroupId), ct);
}
