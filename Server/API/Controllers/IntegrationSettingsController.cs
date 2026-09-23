using Application.Features.Integrations.Commands;
using Application.Features.Integrations.Models;
using Application.Features.Integrations.Queries;
using Application.Features.Routing.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Policy = "Admin")]
[Route("api/settings/integrations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IntegrationSettingsController : BaseController
{
  [HttpGet]
  public Task<IActionResult> Get(CancellationToken ct) =>
    HandleRequest(new GetIntegrationSettingsQuery(), ct);

  [HttpGet("whatsapp/webhook")]
  public Task<IActionResult> WhatsAppWebhook(CancellationToken ct) =>
    HandleRequest(new GetWhatsAppWebhookQuery(), ct);

  [HttpPut("{provider}")]
  [RequestSizeLimit(20_480)]
  public Task<IActionResult> Save(
    string provider,
    IntegrationCredentialsUpdate request,
    CancellationToken ct
  ) =>
    HandleRequest(
      new UpdateIntegrationCredentialsCommand(provider, request),
      ct
    );
}
