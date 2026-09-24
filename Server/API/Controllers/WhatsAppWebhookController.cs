using Application.Features.Messaging.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Meta calls these without a user. The GET echoes a challenge only for the
// carrier's verify token; the POST is read only when its signature matches
// that carrier's app secret. Messaging records all of a notification in
// one transaction.
[AllowAnonymous]
[Route("api/webhooks/whatsapp/{companyKey}")]
public sealed class WhatsAppWebhookController : BaseController
{
  [HttpGet]
  public async Task<IActionResult> Verify(
    string companyKey,
    [FromQuery(Name = "hub.mode")] string? mode,
    [FromQuery(Name = "hub.verify_token")] string? verifyToken,
    [FromQuery(Name = "hub.challenge")] string? challenge,
    CancellationToken ct
  )
  {
    var result = await Mediator.Send(
      new VerifyDriverMessagingWebhookQuery(
        companyKey,
        mode,
        verifyToken,
        challenge
      ),
      ct
    );
    return result.Success
      ? Content(result.Response!, "text/plain")
      : StatusCode(result.StatusCode);
  }

  [HttpPost]
  [RequestSizeLimit(DriverMessagingWebhookHandlers.MaximumBody)]
  public async Task<IActionResult> Receive(
    string companyKey,
    CancellationToken ct
  )
  {
    var result = await Mediator.Send(
      new ReceiveDriverMessagesCommand(
        companyKey,
        Request.Headers["X-Hub-Signature-256"].ToString(),
        Request.Body
      ),
      ct
    );
    return StatusCode(result.StatusCode);
  }
}
