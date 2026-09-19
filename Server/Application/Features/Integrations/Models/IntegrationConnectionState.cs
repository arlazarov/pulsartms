namespace Application.Features.Integrations.Models;

public sealed record IntegrationCredentialFieldState(
  string Name,
  bool Configured
);

public sealed record IntegrationConnectionState(
  string Provider,
  bool Configured,
  bool UsesSavedSettings,
  bool CanRestoreDeployment,
  long Revision,
  DateTime? UpdatedAt,
  IReadOnlyList<IntegrationCredentialFieldState> Fields
);

public sealed record StoredIntegrationCredentials(
  long Revision,
  DateTime? UpdatedAt,
  IntegrationCredentialValues? Values
);

public sealed class IntegrationCredentialsUpdate
{
  public long Revision { get; set; }
  public Dictionary<string, string?>? Fields { get; set; } =
    new(StringComparer.Ordinal);
  public bool RestoreDeployment { get; set; }
}
