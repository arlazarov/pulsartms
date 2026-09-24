using Application.Models;

namespace Application.Features.Messaging.Queries;

// Where an administrator points the messaging provider's webhook for this
// carrier. The path is the provider's ingress route.
public sealed record GetMessagingWebhookQuery
  : IRequest<RequestResponse<MessagingWebhookAddress>>;

public sealed record MessagingWebhookAddress(string Path);

public sealed class GetMessagingWebhookHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  ICurrentCompany companies
)
  : IRequestHandler<
    GetMessagingWebhookQuery,
    RequestResponse<MessagingWebhookAddress>
  >
{
  public async Task<RequestResponse<MessagingWebhookAddress>> Handle(
    GetMessagingWebhookQuery request,
    CancellationToken ct
  )
  {
    if (
      !caller.IsAuthenticated
      || string.IsNullOrEmpty(caller.IdentityUserId)
      || await roles.GetAsync(caller.IdentityUserId, ct) != "Admin"
      || !await db
        .Users.AsNoTracking()
        .AnyAsync(
          x => x.IdentityUserId == caller.IdentityUserId && x.IsActive,
          ct
        )
    )
      return RequestResponse<MessagingWebhookAddress>.Fail(
        "Administrator access is required.",
        403
      );
    var key = await db
      .Companies.AsNoTracking()
      .Where(x => x.Id == companies.Id)
      .Select(x => x.Key)
      .SingleOrDefaultAsync(ct);
    return string.IsNullOrWhiteSpace(key)
      ? RequestResponse<MessagingWebhookAddress>.Fail("Company not found.", 404)
      : RequestResponse<MessagingWebhookAddress>.Ok(
        new($"api/webhooks/whatsapp/{Uri.EscapeDataString(key)}")
      );
  }
}
