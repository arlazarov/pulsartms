using Application.Features.Fleet.Services;
using Application.Models;

namespace Application.Features.Routing.Commands;

// Where an administrator points Meta's webhook for this carrier.
public sealed record GetWhatsAppWebhookQuery
  : IRequest<RequestResponse<WhatsAppWebhookAddress>>;

public sealed record WhatsAppWebhookAddress(string Path);

public sealed class GetWhatsAppWebhookHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  ICurrentCompany companies
)
  : IRequestHandler<
    GetWhatsAppWebhookQuery,
    RequestResponse<WhatsAppWebhookAddress>
  >
{
  public async Task<RequestResponse<WhatsAppWebhookAddress>> Handle(
    GetWhatsAppWebhookQuery request,
    CancellationToken ct
  )
  {
    if (!await FleetConfigurationAccess.IsAdminAsync(db, caller, roles, ct))
      return RequestResponse<WhatsAppWebhookAddress>.Fail(
        "Administrator access is required.",
        403
      );
    var key = await db
      .Companies.AsNoTracking()
      .Where(x => x.Id == companies.Id)
      .Select(x => x.Key)
      .SingleOrDefaultAsync(ct);
    return string.IsNullOrWhiteSpace(key)
      ? RequestResponse<WhatsAppWebhookAddress>.Fail("Company not found.", 404)
      : RequestResponse<WhatsAppWebhookAddress>.Ok(
        new($"api/webhooks/whatsapp/{Uri.EscapeDataString(key)}")
      );
  }
}
