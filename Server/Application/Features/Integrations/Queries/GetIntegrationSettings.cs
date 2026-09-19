using Application.Features.Integrations.Models;
using Application.Features.Integrations.Services;
using Application.Models;

namespace Application.Features.Integrations.Queries;

public sealed record GetIntegrationSettingsQuery
  : IRequest<RequestResponse<IReadOnlyList<IntegrationConnectionState>>>;

public sealed class GetIntegrationSettingsHandler(
  IntegrationSettingsService settings,
  ICurrentUser currentUser,
  IUserRoleService roles
)
  : IRequestHandler<
    GetIntegrationSettingsQuery,
    RequestResponse<IReadOnlyList<IntegrationConnectionState>>
  >
{
  public async Task<
    RequestResponse<IReadOnlyList<IntegrationConnectionState>>
  > Handle(GetIntegrationSettingsQuery request, CancellationToken ct)
  {
    if (
      !currentUser.IsAuthenticated
      || string.IsNullOrWhiteSpace(currentUser.IdentityUserId)
    )
      return RequestResponse<IReadOnlyList<IntegrationConnectionState>>.Fail(
        "Unauthorized.",
        401
      );
    if (await roles.GetAsync(currentUser.IdentityUserId, ct) != "Admin")
      return RequestResponse<IReadOnlyList<IntegrationConnectionState>>.Fail(
        "Admin access is required.",
        403
      );
    var result = new List<IntegrationConnectionState>();
    foreach (var provider in IntegrationProviderCatalog.Providers)
      result.Add(await settings.GetStateAsync(provider, ct));
    return RequestResponse<IReadOnlyList<IntegrationConnectionState>>.Ok(
      result
    );
  }
}
