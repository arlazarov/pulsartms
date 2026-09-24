using Application.Features.Integrations.Models;
using Application.Features.Integrations.Services;
using Application.Models;

namespace Application.Features.Integrations.Commands;

public sealed record UpdateIntegrationCredentialsCommand(
  string Provider,
  IntegrationCredentialsUpdate Update
) : IRequest<RequestResponse<IntegrationConnectionState>>, IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (!IntegrationProviderCatalog.Contains(Provider))
      yield return "Unsupported integration.";
    if (Update is null)
      yield return "The credentials to save are missing.";
    else
    {
      if (Update.Revision is < 0 or long.MaxValue)
        yield return "Reopen the integration before saving it.";
      if (Update.Fields is null)
        yield return "The credential fields are missing.";
      else if (IntegrationProviderCatalog.Contains(Provider) && !ValidFields())
        yield return "Credential fields are invalid.";
    }
  }

  // Only the provider's own catalogued fields, each a single unbroken
  // secret - and none of them supplied at all when the request is asking
  // for the stored ones to be put back. A save may carry every field the
  // provider has (WhatsApp has four), counted by key even when blank.
  private bool ValidFields()
  {
    var fields = Update.Fields!;
    var allowed = IntegrationProviderCatalog.Fields(Provider);
    if (
      fields.Count > allowed.Count
      || fields.Keys.Any(field => !allowed.Contains(field))
    )
      return false;
    if (
      Update.RestoreDeployment
      && fields.Values.Any(value => !string.IsNullOrWhiteSpace(value))
    )
      return false;
    return fields.Values.All(value =>
      value is null
      || value.Length <= 4096
        && (
          string.IsNullOrWhiteSpace(value)
          || !value.Trim().Any(char.IsWhiteSpace) && !value.Any(char.IsControl)
        )
    );
  }
}

public sealed class UpdateIntegrationCredentialsHandler(
  IntegrationSettingsService settings,
  ICurrentUser currentUser,
  IUserRoleService roles
)
  : IRequestHandler<
    UpdateIntegrationCredentialsCommand,
    RequestResponse<IntegrationConnectionState>
  >
{
  public async Task<RequestResponse<IntegrationConnectionState>> Handle(
    UpdateIntegrationCredentialsCommand request,
    CancellationToken ct
  )
  {
    if (
      !currentUser.IsAuthenticated
      || string.IsNullOrWhiteSpace(currentUser.IdentityUserId)
    )
      return RequestResponse<IntegrationConnectionState>.Fail(
        "Unauthorized.",
        401
      );
    if (await roles.GetAsync(currentUser.IdentityUserId, ct) != "Admin")
      return RequestResponse<IntegrationConnectionState>.Fail(
        "Admin access is required.",
        403
      );
    return await settings.SaveAsync(request.Provider, request.Update, ct);
  }
}
