using Application.Interfaces;
using Application.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Application.Behaviors;

public sealed class AdminAuditBehavior<TRequest, TResponse>(ICurrentUser currentUser, ILogger<AdminAuditBehavior<TRequest, TResponse>> logger)
  : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
  private static readonly HashSet<string> Audited = ["RegisterUserCommand", "UpdateUserCommand", "DeleteUserCommand", "UpdatePlanningSettingsCommand", "UpdateDispatchSettingsCommand", "UpdateIntegrationCredentialsCommand"];
  public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
  {
    var name = typeof(TRequest).Name;
    if (!Audited.Contains(name)) return await next();
    // Allowlist only non-secret changes; never serialize a command or profile.
    var fields = typeof(TRequest).GetProperties()
      .Where(p => p.Name is "Id" or "Role" or "IsActive" or "UseIfta" or "MaxDetourMinutes" or "ReserveGallons" or "FillPercent")
      .ToDictionary(p => p.Name, p => p.GetValue(request));
    if (request is Features.Routing.Commands.UpdatePlanningSettingsCommand settings)
    {
      fields["Revision"] = settings.Settings.Revision;
      foreach (var property in settings.Settings.Preferences.GetType().GetProperties()
        .Where(p => p.Name is "UseIfta" or "MaxDetourMinutes" or "ReserveGallons" or "FillPercent"))
        fields[property.Name] = property.GetValue(settings.Settings.Preferences);
    }
    var outcome = "failed";
    if (request is Features.Integrations.Commands.UpdateIntegrationCredentialsCommand integration)
    {
      fields["Provider"] = Features.Integrations.Models.IntegrationProviderCatalog.Contains(integration.Provider) ? integration.Provider : "unsupported";
      fields["Revision"] = integration.Update?.Revision;
      fields["RestoreDeployment"] = integration.Update?.RestoreDeployment;
    }
    try
    {
      var response = await next();
      outcome = response is IRequestOutcome { Success: false } ? "rejected" : "completed";
      return response;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = "cancelled"; throw; }
    finally
    {
      logger.LogInformation("AdminAudit Actor={Actor} Operation={Operation} Outcome={Outcome} Changes={Changes} TraceId={TraceId}",
        currentUser.IdentityUserId, name, outcome, JsonSerializer.Serialize(fields), Activity.Current?.TraceId.ToString());
    }
  }
}
