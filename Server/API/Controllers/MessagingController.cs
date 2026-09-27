using Application.Features.Dispatch.Documents;
using Application.Features.Eta.Queries;
using Application.Features.Execution.Queries;
using Application.Features.Fleet.Queries;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Queries;
using Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Conversations with drivers. "changes" carries only "conversation changed"
// signals, one reader per browser; the conversation itself is read through
// the ordinary endpoints.
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
    CancellationToken cancellationToken,
    [FromQuery] bool archived = false
  ) =>
    HandleRequest(
      new GetInboxQuery(
        unread,
        search,
        afterAt is { } at && afterId is { } id ? new(at.UtcDateTime, id) : null,
        InChosenGroup: true,
        Archived: archived
      ),
      cancellationToken
    );

  // Messages by words, days (in the browser's time zone) and load, in one
  // conversation or the dispatcher's driver group; afterSentAt,
  // afterCreatedAt and afterId continue below a page.
  [HttpGet("search")]
  public Task<IActionResult> Search(
    [FromQuery] Guid? conversationId,
    [FromQuery] string? text,
    [FromQuery] DateOnly? from,
    [FromQuery] DateOnly? to,
    [FromQuery] string? timeZone,
    [FromQuery] int? load,
    [FromQuery] DateTimeOffset? afterSentAt,
    [FromQuery] DateTimeOffset? afterCreatedAt,
    [FromQuery] Guid? afterId,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new SearchMessagesQuery(
        conversationId,
        text,
        from,
        to,
        timeZone,
        load,
        afterSentAt is { } sent
        && afterCreatedAt is { } created
        && afterId is { } message
          ? new(sent.UtcDateTime, created.UtcDateTime, message)
          : null,
        InChosenGroup: conversationId is null
      ),
      cancellationToken
    );

  public sealed record BroadcastBody(
    Guid IdempotencyKey,
    BroadcastRequest Request
  );

  // One message to several drivers, each in their own conversation:
  // preview writes nothing; create is idempotent on its key; cancel takes
  // back only what has not gone.
  [HttpPost("broadcasts/preview")]
  public Task<IActionResult> PreviewBroadcast(
    BroadcastRequest request,
    CancellationToken cancellationToken
  ) => HandleRequest(new PreviewBroadcastQuery(request), cancellationToken);

  [HttpPost("broadcasts")]
  public Task<IActionResult> CreateBroadcast(
    BroadcastBody body,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new CreateBroadcastCommand(body.IdempotencyKey, body.Request),
      cancellationToken
    );

  [HttpGet("broadcasts/{id:guid}")]
  public Task<IActionResult> Broadcast(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetBroadcastQuery(id), cancellationToken);

  [HttpPost("broadcasts/{id:guid}/cancel")]
  public Task<IActionResult> CancelBroadcast(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new CancelBroadcastCommand(id), cancellationToken);

  // Drivers a chat can be started with, in the dispatcher's driver group.
  [HttpGet("drivers")]
  public Task<IActionResult> Drivers(
    [FromQuery] string? search,
    [FromQuery] string? afterName,
    [FromQuery] Guid? afterId,
    [FromQuery] bool withoutConversation,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new GetMessagingDriversQuery(
        search,
        afterName is { } name && afterId is { } id ? new(name, id) : null,
        InChosenGroup: true,
        WithoutConversation: withoutConversation
      ),
      cancellationToken
    );

  // The driver's conversation, created when there is none; sends nothing.
  [HttpPost("drivers/{id:guid}/conversation")]
  public Task<IActionResult> OpenDriverConversation(
    Guid id,
    CancellationToken cancellationToken
  ) => HandleRequest(new OpenDriverConversationCommand(id), cancellationToken);

  // Who the conversation is with (Messaging), what they are driving
  // (Execution), their hours of service (Fleet's shared snapshot) and when
  // their duty status began (Eta's history): four owners' answers, side
  // by side, in one response. Hours and Duty are null when no driver is
  // linked.
  public sealed record ConversationContextView(
    Guid? DriverId,
    string? DriverName,
    string State,
    IReadOnlyList<DriverTruck> Trucks,
    IReadOnlyList<DriverLoad> Loads,
    DriverHoursView? Hours,
    DriverDutyView? Duty
  )
  {
    public int OmittedLoads { get; init; }
    public int OmittedConflicts { get; init; }
  }

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
    DriverHoursView? hours = null;
    DriverDutyView? duty = null;
    if (who.DriverId is { } linked)
    {
      var read = await Mediator.Send(
        new GetDriverHosQuery(linked),
        cancellationToken
      );
      if (!read.Success)
        return StatusCode(read.StatusCode, read);
      hours = read.Response;
      var status = await Mediator.Send(
        new GetDriverDutyStatusQuery(
          linked,
          driving.Trucks.Count == 1 ? driving.Trucks[0].Id : null
        ),
        cancellationToken
      );
      if (!status.Success)
        return StatusCode(status.StatusCode, status);
      duty = status.Response;
    }
    return Ok(
      RequestResponse<ConversationContextView>.Ok(
        new(
          who.DriverId,
          who.DriverName,
          driving.State,
          driving.Trucks,
          driving.Loads,
          hours,
          duty
        )
        {
          OmittedLoads = driving.OmittedLoads,
          OmittedConflicts = driving.OmittedConflicts,
        }
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
    [FromQuery] Guid? around,
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
        seen,
        around
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

  // Answers when a change commits, or empty after about 20 seconds; see
  // WaitMessagingChangesQuery. Firebase Hosting holds a streamed response
  // back until it ends, so this is a request that ends. Never cached: the
  // answer is this caller's alone.
  [HttpGet("changes")]
  public Task<IActionResult> Changes(
    [FromQuery] Guid? mailbox,
    CancellationToken cancellationToken
  )
  {
    Response.Headers.CacheControl = "no-store";
    return HandleRequest(
      new WaitMessagingChangesQuery(mailbox),
      cancellationToken
    );
  }
}
