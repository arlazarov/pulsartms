using Application.Features.Integrations.Commands;
using Application.Features.Integrations.Models;
using Application.Features.Integrations.Queries;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Queries;
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
    HandleRequest(new GetMessagingWebhookQuery(), ct);

  // Templates Meta approved for the number the company sends from now.
  [HttpGet("whatsapp/templates")]
  public Task<IActionResult> Templates(CancellationToken ct) =>
    HandleRequest(new GetApprovedTemplatesQuery(), ct);

  public sealed record TemplateRequest(
    string Name,
    string Language,
    int Parameters,
    string Text
  );

  [HttpPost("whatsapp/templates")]
  [RequestSizeLimit(8_192)]
  public Task<IActionResult> ApproveTemplate(
    TemplateRequest request,
    CancellationToken ct
  ) =>
    HandleRequest(
      new ApproveTemplateCommand(
        request.Name,
        request.Language,
        request.Parameters,
        request.Text
      ),
      ct
    );

  [HttpDelete("whatsapp/templates/{id:guid}")]
  public Task<IActionResult> WithdrawTemplate(Guid id, CancellationToken ct) =>
    HandleRequest(new WithdrawTemplateCommand(id), ct);

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
