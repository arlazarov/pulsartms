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

  // A provider's own fields, at most three of them, each a single
  // unbroken secret - and none of them supplied at all when the request
  // is asking for the stored ones to be put back.
  private bool ValidFields()
  {
    var fields = Update.Fields;
    if (
      fields.Count > 3
      || fields.Keys.Any(field =>
        !IntegrationProviderCatalog.Fields(Provider).Contains(field)
      )
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
