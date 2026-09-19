using Application.Features.Dispatch.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Dispatch")]
[Route("api/dispatch/{id:guid}/documents")]
public sealed class DispatchDocumentsController : BaseController
{
  [HttpGet]
  public Task<IActionResult> List(Guid id, CancellationToken ct) =>
    HandleRequest(new GetDispatchDocumentsQuery(id), ct);

  [HttpPost]
  [RequestSizeLimit(8 * 1024 * 1024)]
  public Task<IActionResult> Upload(
    Guid id,
    [FromBody] UploadDispatchDocumentRequest request,
    CancellationToken ct
  ) => HandleRequest(new UploadDispatchDocumentCommand(id, request), ct);

  [HttpGet("{documentId:guid}")]
  public Task<IActionResult> Download(
    Guid id,
    Guid documentId,
    CancellationToken ct
  )
  {
    Response.Headers.CacheControl = "no-store";
    return HandleRequest(new DownloadDispatchDocumentQuery(id, documentId), ct);
  }
}
