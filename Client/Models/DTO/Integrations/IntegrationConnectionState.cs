namespace Client.Models.DTO.Integrations;

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

public sealed class IntegrationCredentialsUpdate
{
  public long Revision { get; set; }
  public Dictionary<string, string?> Fields { get; set; } =
    new(StringComparer.Ordinal);
  public bool RestoreDeployment { get; set; }
}

public sealed record WhatsAppWebhookAddress(string Path);
