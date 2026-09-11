using Application.Features.Integrations.Models;
using Application.Features.Integrations.Services;
using Application.Models;

namespace Application.Features.Integrations.Commands;

public sealed record UpdateIntegrationCredentialsCommand(string Provider, IntegrationCredentialsUpdate Update)
  : IRequest<RequestResponse<IntegrationConnectionState>>;

public sealed class UpdateIntegrationCredentialsValidator : AbstractValidator<UpdateIntegrationCredentialsCommand>
{
  public UpdateIntegrationCredentialsValidator()
  {
    RuleFor(request => request.Provider).Must(IntegrationProviderCatalog.Contains).WithMessage("Unsupported integration.");
    RuleFor(request => request.Update).NotNull();
    When(request => request.Update is not null, () =>
    {
      RuleFor(request => request.Update.Revision).GreaterThanOrEqualTo(0).LessThan(long.MaxValue);
      RuleFor(request => request.Update.Fields).NotNull();
      RuleFor(request => request).Must(HasValidFields).WithMessage("Credential fields are invalid.").OverridePropertyName("Fields");
    });
  }

  private static bool HasValidFields(UpdateIntegrationCredentialsCommand request)
  {
    if (!IntegrationProviderCatalog.Contains(request.Provider) || request.Update.Fields is null) return false;
    var fields = request.Update.Fields;
    if (fields.Count > 3 || fields.Keys.Any(field => !IntegrationProviderCatalog.Fields(request.Provider).Contains(field))) return false;
    if (request.Update.RestoreDeployment && fields.Values.Any(value => !string.IsNullOrWhiteSpace(value))) return false;
    return fields.Values.All(value => value is null || value.Length <= 4096
      && (string.IsNullOrWhiteSpace(value) || !value.Trim().Any(char.IsWhiteSpace) && !value.Any(char.IsControl)));
  }
}

public sealed class UpdateIntegrationCredentialsHandler(IntegrationSettingsService settings, ICurrentUser currentUser, IUserRoleService roles)
  : IRequestHandler<UpdateIntegrationCredentialsCommand, RequestResponse<IntegrationConnectionState>>
{
  public async Task<RequestResponse<IntegrationConnectionState>> Handle(UpdateIntegrationCredentialsCommand request, CancellationToken ct)
  {
    if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.IdentityUserId))
      return RequestResponse<IntegrationConnectionState>.Fail("Unauthorized.", 401);
    if (await roles.GetAsync(currentUser.IdentityUserId, ct) != "Admin")
      return RequestResponse<IntegrationConnectionState>.Fail("Admin access is required.", 403);
    return await settings.SaveAsync(request.Provider, request.Update, ct);
  }
}
