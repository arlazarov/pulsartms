namespace Application.Features.Integrations.Models;

public static class IntegrationProviderCatalog
{
  public const string Torque = "torqueai";
  public const string Samsara = "samsara";
  public const string GoogleEmail = "google-email";
  public const string WhatsApp = "whatsapp";
  public static IReadOnlyList<string> Providers { get; } =
    Array.AsReadOnly(new[] { Torque, Samsara, GoogleEmail, WhatsApp });
  private static readonly IReadOnlyList<string> ApiKeyFields = Array.AsReadOnly(
    new[] { "apiKey" }
  );
  private static readonly IReadOnlyList<string> EmailFields = Array.AsReadOnly(
    new[] { "clientId", "clientSecret", "refreshToken" }
  );
  private static readonly IReadOnlyList<string> WhatsAppFields =
    Array.AsReadOnly(
      new[] { "phoneNumberId", "accessToken", "appSecret", "verifyToken" }
    );

  public static bool Contains(string? provider) =>
    provider is Torque or Samsara or GoogleEmail or WhatsApp;

  public static IReadOnlyList<string> Fields(string provider) =>
    provider switch
    {
      Torque or Samsara => ApiKeyFields,
      GoogleEmail => EmailFields,
      WhatsApp => WhatsAppFields,
      _ => throw new ArgumentException(
        "Unsupported integration.",
        nameof(provider)
      ),
    };

  public static bool IsConfigured(
    string provider,
    IntegrationCredentialValues values
  ) =>
    Fields(provider)
      .All(field => !string.IsNullOrWhiteSpace(values.Get(field)));
}
