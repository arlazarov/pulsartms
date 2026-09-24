using Application.Features.Messaging.Commands;
using Application.Features.Routing.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Meta calls these without a user. The GET echoes a challenge only for the
// carrier's verify token; the POST is read only when its signature matches
// that carrier's app secret. One notification has two owners: Messaging
// records what drivers wrote and the replies' statuses, then fuel planning
// applies its sent plans' statuses and reply windows. Each commits on its
// own and both only move forward, so the provider's retry of a failed
// notification settles both.
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
    var received = await Mediator.Send(
      new ReceiveDriverMessagesCommand(
        companyKey,
        Request.Headers["X-Hub-Signature-256"].ToString(),
        Request.Body
      ),
      ct
    );
    if (!received.Success || received.Response is not { } verified)
      return StatusCode(received.StatusCode);
    var result = await Mediator.Send(
      new ApplyFuelPlanMessageEventsCommand(
        verified.Company,
        verified.Notification
      ),
      ct
    );
    return StatusCode(result.StatusCode);
  }
}
