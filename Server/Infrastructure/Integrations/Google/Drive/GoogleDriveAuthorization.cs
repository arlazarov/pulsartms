using Application.Storage;

namespace Infrastructure.Integrations.Google.Drive;

// Connecting a company's Google Drive: consent with PKCE for the drive.file
// scope, so PulsR reaches only files it created and folders an
// administrator picks for it. Nothing is created in the account here.
public sealed class GoogleDriveAuthorization(
  HttpClient http,
  GoogleDriveClient client
) : IStorageAuthorization
{
  public string Kind => StorageKinds.GoogleDrive;
  public bool IsConfigured => client.IsConfigured;

  public Uri AuthorizationUrl(string state, string codeChallenge) =>
    new(
      "https://accounts.google.com/o/oauth2/v2/auth?"
        + string.Join(
          '&',
          new Dictionary<string, string>
          {
            ["client_id"] = client.ClientId!,
            ["redirect_uri"] = client.RedirectUri!,
            ["response_type"] = "code",
            ["scope"] = GoogleDriveClient.Scope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "false",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
          }.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")
        )
    );

  public async Task<StorageGrant?> ExchangeAsync(
    string code,
    string codeVerifier,
    CancellationToken ct
  )
  {
    var answer = await client.TokenAsync(
      http,
      [
        new("grant_type", "authorization_code"),
        new("code", code),
        new("code_verifier", codeVerifier),
        new("redirect_uri", client.RedirectUri ?? ""),
      ],
      ct
    );
    return answer?.RefreshToken is { Length: > 0 } refresh
      ? new(GoogleDriveClient.Serialize(refresh))
      : null;
  }
}
