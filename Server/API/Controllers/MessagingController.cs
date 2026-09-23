using System.Text.Json;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Queries;
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
  public sealed record ReadRequest(DateTime Through);

  public sealed record SendRequest(
    string Body,
    Guid IdempotencyKey,
    Guid? LastSeenMessageId,
    bool Confirm
  );

  [HttpGet("inbox")]
  public Task<IActionResult> Inbox(
    [FromQuery] bool unread,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetInboxQuery(unread), cancellationToken);

  [HttpGet("conversations/{id:guid}")]
  public Task<IActionResult> Conversation(
    Guid id,
    [FromQuery] DateTime? before,
    CancellationToken cancellationToken
  ) => HandleRequest(new GetConversationQuery(id, before), cancellationToken);

  [HttpPost("conversations/{id:guid}/read")]
  public Task<IActionResult> Read(
    Guid id,
    ReadRequest request,
    CancellationToken cancellationToken
  ) =>
    HandleRequest(
      new MarkConversationReadCommand(id, request.Through),
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
