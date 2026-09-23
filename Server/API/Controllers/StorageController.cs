using Application.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Where the company keeps its files. Administrators choose and connect
// storage; the provider's redirect back is anonymous and trusted only
// through the single-use state it carries.
[Authorize(Policy = "Admin")]
[Route("api/storage")]
public sealed class StorageController : BaseController
{
  public sealed record ConnectRequest(string Kind, string? Name);

  public sealed record RevisionRequest(long ExpectedRevision);

  [HttpGet]
  public Task<IActionResult> Get(CancellationToken cancellationToken) =>
    HandleRequest(new GetStorageSettingsQuery(), cancellationToken);

  public sealed record LayoutRequest(
    string LoadsFolder,
    string LoadTemplate,
    string CancelledSuffix,
    string InboxFolder,
    long ExpectedRevision
  );

  [HttpGet("layout")]
  public Task<IActionResult> Layout(CancellationToken cancellationToken) =>
    HandleRequest(new GetStorageLayoutQuery(), cancellationToken);

  [HttpPut("layout")]
  public Task<IActionResult> SaveLayout(
    LayoutRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new UpdateStorageLayoutCommand(
        request.LoadsFolder,
        request.LoadTemplate,
        request.CancelledSuffix,
        request.InboxFolder,
        request.ExpectedRevision
      ),
      cancellationToken
    );

  [HttpPost("connections")]
  public Task<IActionResult> Connect(
    ConnectRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new BeginStorageConnectionCommand(request.Kind, request.Name),
      cancellationToken
    );

  [HttpPost("connections/{id:guid}/default")]
  public Task<IActionResult> MakeDefault(
    Guid id,
    RevisionRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SetDefaultStorageCommand(id, request.ExpectedRevision),
      cancellationToken
    );

  [HttpPost("connections/{id:guid}/disconnect")]
  public Task<IActionResult> Disconnect(
    Guid id,
    RevisionRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new DisconnectStorageCommand(id, request.ExpectedRevision),
      cancellationToken
    );

  public sealed record RootRequest(string FolderId, long ExpectedRevision);

  [HttpPost("connections/{id:guid}/picker")]
  public Task<IActionResult> Picker(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetStoragePickerCommand(id), cancellationToken);

  [HttpPost("connections/{id:guid}/root")]
  public Task<IActionResult> ChooseRoot(
    Guid id,
    RootRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new ChooseStorageRootCommand(
        id,
        request.FolderId,
        request.ExpectedRevision
      ),
      cancellationToken
    );

  [HttpPost("connections/{id:guid}/check")]
  public Task<IActionResult> Check(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new CheckStorageCommand(id), cancellationToken);

  [AllowAnonymous]
  [HttpGet("connections/callback")]
  public async Task<IActionResult> Callback(
    [FromQuery] string? state,
    [FromQuery] string? code,
    [FromQuery] string? error,
    CancellationToken cancellationToken
  )
  {
    var result = await Mediator.Send(
      new CompleteStorageConnectionCommand(state, code, error),
      cancellationToken
    );
    return Redirect(
      $"/settings?storage={Uri.EscapeDataString(result.Response ?? "failed")}"
    );
  }
}
