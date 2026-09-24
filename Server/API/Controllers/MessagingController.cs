using System.Text.Json;
using Application.Features.Dispatch.Documents;
using Application.Features.Execution.Queries;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Queries;
using Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Conversations with drivers. The event stream carries only "conversation
// changed" signals, one stream per browser; the conversation itself is read
// through the ordinary endpoints.
[Authorize(Policy = "Dispatch")]
[Route("api/messaging")]
public sealed class MessagingController : BaseController
{
  public sealed record ReadRequest(long Revision);

  public sealed record DriverRequest(Guid? DriverId, long Revision);

  public sealed record FileRequest(
    Guid? DispatchId,
    int? LoadNumber,
    string Kind
  );

  public sealed record SendRequest(
    string Body,
    Guid IdempotencyKey,
    Guid? LastSeenMessageId,
    bool Confirm
  );

  [HttpGet("inbox")]
  public Task<IActionResult> Inbox(
    [FromQuery] bool unread,
    [FromQuery] string? search,
    [FromQuery] DateTimeOffset? afterAt,
    [FromQuery] Guid? afterId,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new GetInboxQuery(
        unread,
        search,
        afterAt is { } at && afterId is { } id ? new(at.UtcDateTime, id) : null
      ),
      cancellationToken
    );

  // Who the conversation is with (Messaging) beside what they are driving
  // (Execution): two owners' answers, side by side, in one response.
  public sealed record ConversationContextView(
    Guid? DriverId,
    string? DriverName,
    string State,
    IReadOnlyList<DriverTruck> Trucks,
    IReadOnlyList<DriverLoad> Loads
  );

  [HttpGet("conversations/{id:guid}/context")]
  public async Task<IActionResult> Context(
    Guid id,
    CancellationToken cancellationToken
  )
  {
    var driver = await Mediator.Send(
      new GetConversationDriverQuery(id),
      cancellationToken
    );
    if (!driver.Success || driver.Response is not { } who)
      return StatusCode(driver.StatusCode, driver);
    var work = await Mediator.Send(
      new GetDriverWorkQuery(who.DriverId),
      cancellationToken
    );
    if (!work.Success || work.Response is not { } driving)
      return StatusCode(work.StatusCode, work);
    return Ok(
      RequestResponse<ConversationContextView>.Ok(
        new(
          who.DriverId,
          who.DriverName,
          driving.State,
          driving.Trucks,
          driving.Loads
        )
      )
    );
  }

  [HttpPut("conversations/{id:guid}/driver")]
  public Task<IActionResult> Driver(
    Guid id,
    DriverRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SetConversationDriverCommand(id, request.DriverId, request.Revision),
      cancellationToken
    );

  [HttpPost("attachments/{id:guid}/file")]
  public Task<IActionResult> FileAttachment(
    Guid id,
    FileRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new FileMessageAttachmentCommand(
        id,
        request.DispatchId,
        request.LoadNumber,
        request.Kind
      ),
      cancellationToken
    );

  [HttpGet("unread")]
  public Task<IActionResult> Unread(CancellationToken cancellationToken) =>
    HandleRequest(new GetUnreadNoticeQuery(), cancellationToken);

  // beforeSentAt, beforeCreatedAt and beforeId continue below a message;
  // before alone is the earlier clients' form, every message older than
  // that time.
  [HttpGet("conversations/{id:guid}")]
  public Task<IActionResult> Conversation(
    Guid id,
    [FromQuery] DateTime? before,
    [FromQuery] DateTimeOffset? beforeSentAt,
    [FromQuery] DateTimeOffset? beforeCreatedAt,
    [FromQuery] Guid? beforeId,
    [FromQuery] long? seen,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new GetConversationQuery(
        id,
        beforeSentAt is { } sent
          && beforeCreatedAt is { } created
          && beforeId is { } message
            ? new(sent.UtcDateTime, created.UtcDateTime, message)
          : before is { } time ? MessageCursor.Older(time)
          : null,
        seen
      ),
      cancellationToken
    );

  [HttpPost("conversations/{id:guid}/read")]
  public Task<IActionResult> Read(
    Guid id,
    ReadRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new MarkConversationReadCommand(id, request.Revision),
      cancellationToken
    );

  [HttpPost("conversations/{id:guid}/messages")]
  public Task<IActionResult> Send(
    Guid id,
    SendRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SendConversationMessageCommand(
        id,
        request.Body,
        request.IdempotencyKey,
        request.LastSeenMessageId,
        request.Confirm
      ),
      cancellationToken
    );

  public sealed record TemplateRequest(
    Guid IdempotencyKey,
    string Name,
    string Language,
    IReadOnlyList<string> Parameters
  );

  // Uploaded forms are buffered by the server; the limit keeps one upload
  // within what an instance can hold.
  public const long MaximumUpload = 16 * 1024 * 1024;

  [HttpPost("conversations/{id:guid}/files")]
  [RequestSizeLimit(MaximumUpload + 64 * 1024)]
  [RequestFormLimits(MultipartBodyLengthLimit = MaximumUpload + 64 * 1024)]
  public async Task<IActionResult> SendFile(
    Guid id,
    IFormFile file,
    [FromForm] string sha256,
    [FromForm] Guid idempotencyKey,
    [FromForm] string? caption,
    [FromForm] Guid? lastSeenMessageId,
    [FromForm] bool confirm,
    CancellationToken cancellationToken
  )
  {
    await using var content = file.OpenReadStream();
    return await HandleRequest(
      new SendConversationFileCommand(
        id,
        idempotencyKey,
        file.FileName,
        file.ContentType,
        file.Length,
        sha256,
        content,
        caption,
        lastSeenMessageId,
        confirm
      ),
      cancellationToken
    );
  }

  [HttpPost("conversations/{id:guid}/templates")]
  public Task<IActionResult> SendTemplate(
    Guid id,
    TemplateRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SendConversationTemplateCommand(
        id,
        request.IdempotencyKey,
        request.Name,
        request.Language,
        request.Parameters
      ),
      cancellationToken
    );

  [HttpGet("templates")]
  public Task<IActionResult> Templates(CancellationToken cancellationToken) =>
    HandleRequest(new GetMessageTemplatesQuery(), cancellationToken);

  [HttpGet("attachments/{id:guid}/content")]
  public async Task<IActionResult> Attachment(
    Guid id,
    CancellationToken cancellationToken
  )
  {
    var result = await Mediator.Send(
      new GetAttachmentContentQuery(id),
      cancellationToken
    );
    if (result.Response is not { } file)
      return StatusCode(result.StatusCode, result);
    Response.Headers.XContentTypeOptions = "nosniff";
    return File(file.Content, file.ContentType, file.Name);
  }

  [HttpPost("messages/{id:guid}/retry")]
  public Task<IActionResult> Retry(
    Guid id,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(new RetryConversationMessageCommand(id), cancellationToken);

  [HttpPost("conversations/{id:guid}/claim")]
  public Task<IActionResult> Claim(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new ClaimConversationCommand(id), cancellationToken);

  [HttpGet("events")]
  public async Task Events(CancellationToken cancellationToken)
  {
    Response.ContentType = "text/event-stream";
    Response.Headers.CacheControl = "no-cache";
    Response.Headers["X-Accel-Buffering"] = "no";
    try
    {
      await foreach (
        var change in Mediator.CreateStream(
          new StreamMessagingEventsQuery(),
          cancellationToken
        )
      )
      {
        await Response.WriteAsync(
          change.ConversationId == Guid.Empty
            ? ": keep-alive\n\n"
            : "data: "
              + JsonSerializer.Serialize(
                new { change.ConversationId, change.Revision },
                JsonSerializerOptions.Web
              )
              + "\n\n",
          cancellationToken
        );
        await Response.Body.FlushAsync(cancellationToken);
      }
    }
    catch (OperationCanceledException)
      when (cancellationToken.IsCancellationRequested) { }
  }
}
