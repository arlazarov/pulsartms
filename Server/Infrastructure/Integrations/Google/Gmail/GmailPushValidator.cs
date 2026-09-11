using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.Google.Gmail;

public sealed class GmailPushValidator(IConfiguration configuration) : Application.Features.Fuel.Interfaces.IGmailPushValidator
{
  public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Gmail:PushAudience"])
    && !string.IsNullOrWhiteSpace(configuration["Gmail:PushServiceAccountEmail"])
    && !string.IsNullOrWhiteSpace(configuration["Gmail:MailboxEmail"]);

  public async Task<bool> ValidateAsync(string authorization)
  {
    if (!IsConfigured || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
      return false;
    try
    {
      var payload = await GoogleJsonWebSignature.ValidateAsync(authorization[7..],
        new GoogleJsonWebSignature.ValidationSettings { Audience = [configuration["Gmail:PushAudience"]!] });
      return payload.EmailVerified && string.Equals(payload.Email,
        configuration["Gmail:PushServiceAccountEmail"], StringComparison.OrdinalIgnoreCase);
    }
    catch (InvalidJwtException) { return false; }
  }

  public bool IsExpectedMailbox(string? email) => !string.IsNullOrWhiteSpace(email)
    && string.Equals(email, configuration["Gmail:MailboxEmail"], StringComparison.OrdinalIgnoreCase);
}
